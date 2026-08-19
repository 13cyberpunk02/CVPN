using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CVPN.Models;
using CVPN.Models.Enums;

namespace CVPN.Services;

/// <summary>
/// Собирает config.json под sing-box 1.12+.
///
/// Что важно знать про эту версию:
///  • geosite/geoip удалены - вместо них удалённые rule-set в формате .srs;
///  • спецвыходы block/dns устарели - вместо них действия правил reject/hijack-dns;
///  • legacy-поля inbound (sniff, domain_strategy) заменены действием sniff в маршрутах;
///  • DNS-серверы описываются новым форматом с полями type/server.
/// </summary>
public static class ConfigBuilder
{
    /// <summary>Тег селектора. На него смотрят все правила маршрутизации.</summary>
    public const string ProxyTag = "proxy";

    /// <summary>Автовыбор быстрейшего сервера (urltest).</summary>
    public const string AutoTag = "auto";

    public const string DirectTag = "direct";

    private const string GeositeBase = "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set";
    private const string GeoipBase = "https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set";

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Все серверы попадают в конфиг сразу, а выбор делает селектор с тегом proxy.
    /// Благодаря этому переключение сервера идёт через Clash API за миллисекунды,
    /// без перезапуска ядра и обрыва туннеля.
    /// </summary>
    public static string Build(
        IReadOnlyList<ServerProfile> profiles,
        ServerProfile active,
        IEnumerable<RouteRule> rules,
        AppSettings settings)
    {
        var servers = profiles.Count > 0 ? profiles : [active];
        var tags = BuildTags(servers);

        var activeTag = tags.TryGetValue(active, out var t) ? t : tags.Values.First();

        var rulesList = rules.Where(r => r.Enabled).ToList();
        var ruleSets = new JsonArray();
        var seenSets = new HashSet<string>(StringComparer.Ordinal);

        var dns = BuildDns(rulesList, settings, ruleSets, seenSets);
        var route = BuildRoute(rulesList, settings, ruleSets, seenSets);

        var outbounds = new JsonArray();

        foreach (var server in servers)
        {
            var outbound = BuildOutbound(server);
            outbound["tag"] = tags[server];
            outbounds.Add(outbound);
        }

        var members = new JsonArray();
        var hasAuto = servers.Count > 1;

        if (hasAuto)
        {
            members.Add(AutoTag);

            outbounds.Add(new JsonObject
            {
                ["type"] = "urltest",
                ["tag"] = AutoTag,
                ["outbounds"] = new JsonArray(servers.Select(x => (JsonNode)tags[x]!).ToArray()),
                ["url"] = "https://cp.cloudflare.com/generate_204",
                ["interval"] = "3m",
                // Меняем сервер, только если новый заметно быстрее - иначе туннель
                // будет прыгать между узлами с близкой задержкой
                ["tolerance"] = 50
            });
        }

        foreach (var server in servers) members.Add(tags[server]);

        outbounds.Add(new JsonObject
        {
            ["type"] = "selector",
            ["outbounds"] = members,
            ["default"] = settings.AutoSelectFastest && hasAuto ? AutoTag : activeTag,
            // Живые соединения не рвём: переключение затронет только новые
            ["interrupt_exist_connections"] = false
        });

        outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = DirectTag });

        var root = new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["level"] = settings.LogLevel,
                ["timestamp"] = true
            },
            ["dns"] = dns,
            ["inbounds"] = BuildInbounds(settings),
            ["outbounds"] = outbounds,
            ["route"] = route,
            ["experimental"] = new JsonObject
            {
                ["cache_file"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["path"] = AppPaths.CacheFile
                },
                ["clash_api"] = new JsonObject
                {
                    ["external_controller"] = $"127.0.0.1:{settings.ClashApiPort}"
                }
            }
        };

        return root.ToJsonString(Pretty);
    }

    public static string Write(
        IReadOnlyList<ServerProfile> profiles,
        ServerProfile active,
        IEnumerable<RouteRule> rules,
        AppSettings settings)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.GeneratedConfig, Build(profiles, active, rules, settings));
        return AppPaths.GeneratedConfig;
    }

    /// <summary>
    /// Тег outbound'а строится из названия профиля. Ядру нужны уникальные теги,
    /// поэтому совпадения получают числовой суффикс.
    /// </summary>
    public static Dictionary<ServerProfile, string> BuildTags(IReadOnlyList<ServerProfile> profiles)
    {
        var result = new Dictionary<ServerProfile, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ProxyTag, AutoTag, DirectTag };

        foreach (var profile in profiles)
        {
            var basis = Sanitize(profile.Name.Length > 0 ? profile.Name : profile.Host);
            var tag = basis;

            for (var n = 2; !used.Add(tag); n++) tag = $"{basis}-{n}";

            result[profile] = tag;
        }

        return result;
    }

    private static string Sanitize(string value)
    {
        var safe = new string(value
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray()).Trim('-');

        while (safe.Contains("--", StringComparison.Ordinal))
            safe = safe.Replace("--", "-", StringComparison.Ordinal);

        return safe.Length > 0 ? safe : "server";
    }

    // ===================== outbound =====================

    /// <summary>
    /// Адрес самого прокси-сервера обязан резолвиться мимо туннеля, иначе получается
    /// замкнутый круг. В 1.12+ это делается полем domain_resolver на outbound'е -
    /// DNS-правило с outbound:"any" устарело и удалено в 1.13.
    /// </summary>
    private static JsonObject DomainResolver() => new()
    {
        ["server"] = "dns-local",
        ["strategy"] = "prefer_ipv4"
    };

    private static JsonObject BuildOutbound(ServerProfile p)
    {
        var outbound = BuildProtocolOutbound(p);
        outbound["domain_resolver"] = DomainResolver();
        return outbound;
    }

    private static JsonObject BuildProtocolOutbound(ServerProfile p) => p.Protocol switch
    {
        ProtocolKind.VlessReality => Vless(p, reality: true),
        ProtocolKind.VlessWs => Vless(p, reality: false),
        ProtocolKind.AnyTls => AnyTls(p),
        ProtocolKind.Naive => Naive(p),
        _ => throw new NotSupportedException($"Протокол {p.Protocol} пока не поддержан")
    };

    private static JsonObject Vless(ServerProfile p, bool reality)
    {
        var tls = new JsonObject
        {
            ["enabled"] = true,
            ["server_name"] = string.IsNullOrWhiteSpace(p.Sni) ? p.Host : p.Sni,
            ["utls"] = new JsonObject { ["enabled"] = true, ["fingerprint"] = "chrome" }
        };

        if (reality)
        {
            tls["reality"] = new JsonObject
            {
                ["enabled"] = true,
                ["public_key"] = p.PublicKey,
                ["short_id"] = p.ShortId
            };
        }

        var outbound = new JsonObject
        {
            ["type"] = "vless",
            ["server"] = p.Host,
            ["server_port"] = p.Port,
            ["uuid"] = p.Uuid,
            ["tls"] = tls
        };

        // flow имеет смысл только с Reality/XTLS; на ws он сломает соединение
        if (reality && !string.IsNullOrWhiteSpace(p.Flow))
            outbound["flow"] = p.Flow;

        if (!reality)
        {
            outbound["transport"] = new JsonObject
            {
                ["type"] = "ws",
                ["path"] = string.IsNullOrWhiteSpace(p.Path) ? "/" : p.Path,
                ["headers"] = new JsonObject
                {
                    ["Host"] = string.IsNullOrWhiteSpace(p.Sni) ? p.Host : p.Sni
                }
            };
        }

        return outbound;
    }

    private static JsonObject AnyTls(ServerProfile p) => new()
    {
        ["type"] = "anytls",
        ["server"] = p.Host,
        ["server_port"] = p.Port,
        ["password"] = p.Password,
        ["idle_session_check_interval"] = "30s",
        ["idle_session_timeout"] = "30s",
        ["min_idle_session"] = 4,
        ["tls"] = new JsonObject
        {
            ["enabled"] = true,
            ["server_name"] = string.IsNullOrWhiteSpace(p.Sni) ? p.Host : p.Sni
        }
    };

    /// <summary>
    /// Naive идёт через Cronet (сетевой стек Chromium). На Windows рядом с sing-box.exe
    /// обязан лежать libcronet.dll, иначе ядро не запустится. TLS-опции здесь урезаны:
    /// insecure, alpn, utls и reality Cronet не принимает.
    /// </summary>
    private static JsonObject Naive(ServerProfile p) => new()
    {
        ["type"] = "naive",
        ["server"] = p.Host,
        ["server_port"] = p.Port,
        ["username"] = p.Username,
        ["password"] = p.Password,
        ["tls"] = new JsonObject
        {
            ["enabled"] = true,
            ["server_name"] = string.IsNullOrWhiteSpace(p.Sni) ? p.Host : p.Sni
        }
    };

    // ===================== inbounds =====================

    private static JsonArray BuildInbounds(AppSettings s)
    {
        var inbounds = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "mixed",
                ["tag"] = "mixed-in",
                ["listen"] = "127.0.0.1",
                ["listen_port"] = s.MixedPort
            }
        };

        if (s.TunEnabled)
        {
            inbounds.Insert(0, new JsonObject
            {
                ["type"] = "tun",
                ["tag"] = "tun-in",
                ["address"] = new JsonArray { "172.19.0.1/30" },
                ["mtu"] = 9000,
                ["auto_route"] = true,
                ["strict_route"] = true,
                ["stack"] = "mixed"
            });
        }

        return inbounds;
    }

    // ===================== route =====================

    private static JsonObject BuildRoute(
        List<RouteRule> rules, AppSettings s, JsonArray ruleSets, HashSet<string> seen)
    {
        var routeRules = new JsonArray
        {
            // Определяем протокол до маршрутизации - иначе доменные правила
            // не сработают для соединений, пришедших по IP из TUN
            new JsonObject { ["action"] = "sniff" },
            new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
            new JsonObject { ["ip_is_private"] = true, ["outbound"] = DirectTag }
        };

        foreach (var rule in rules)
        {
            var node = ToRouteRule(rule, ruleSets, seen);
            if (node is not null) routeRules.Add(node);
        }

        return new JsonObject
        {
            ["rules"] = routeRules,
            ["rule_set"] = ruleSets,
            ["final"] = s.ProxyByDefault ? ProxyTag : DirectTag,
            ["auto_detect_interface"] = true,
            ["default_domain_resolver"] = "dns-local"
        };
    }

    private static JsonObject? ToRouteRule(RouteRule rule, JsonArray ruleSets, HashSet<string> seen)
    {
        var value = rule.Value.Trim();
        if (value.Length == 0) return null;

        var node = new JsonObject();

        switch (rule.Match)
        {
            case MatchKind.Geosite:
            {
                var tag = $"geosite-{value}";
                AddRemoteRuleSet(ruleSets, seen, tag, $"{GeositeBase}/{tag}.srs");
                node["rule_set"] = new JsonArray { tag };
                break;
            }
            case MatchKind.Geoip:
            {
                var tag = $"geoip-{value.ToLowerInvariant()}";
                AddRemoteRuleSet(ruleSets, seen, tag, $"{GeoipBase}/{tag}.srs");
                node["rule_set"] = new JsonArray { tag };
                break;
            }
            case MatchKind.Domain:
                node["domain"] = new JsonArray { value };
                break;
            case MatchKind.DomainSuffix:
                node["domain_suffix"] = new JsonArray { value };
                break;
            case MatchKind.DomainKeyword:
                node["domain_keyword"] = new JsonArray { value };
                break;
            case MatchKind.Process:
                node["process_name"] = new JsonArray { value };
                break;

            case MatchKind.RuleSetRemote:
            {
                var tag = SetTag(value);
                AddRemoteRuleSet(ruleSets, seen, tag, value);
                node["rule_set"] = new JsonArray { tag };
                break;
            }

            case MatchKind.RuleSetLocal:
            {
                var tag = SetTag(value);
                AddLocalRuleSet(ruleSets, seen, tag, value);
                node["rule_set"] = new JsonArray { tag };
                break;
            }
            default:
                return null;
        }

        // reject - действие правила: спецвыход block удалён из ядра
        if (rule.Action == RouteAction.Block)
            node["action"] = "reject";
        else
            node["outbound"] = rule.Action == RouteAction.Direct ? DirectTag : ProxyTag;

        return node;
    }

    /// <summary>
    /// Тег набора выводится из имени файла: ядро требует уникальный идентификатор,
    /// а полный URL или путь для этого не годятся.
    /// </summary>
    private static string SetTag(string source)
    {
        var name = source.Split('/', '\\').LastOrDefault() ?? source;

        if (name.EndsWith(".srs", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray())
            .Trim('-');

        return safe.Length > 0 ? safe : $"set-{Math.Abs(source.GetHashCode()):x}";
    }

    private static void AddLocalRuleSet(JsonArray sets, HashSet<string> seen, string tag, string path)
    {
        if (!seen.Add(tag)) return;

        sets.Add(new JsonObject
        {
            ["type"] = "local",
            ["tag"] = tag,
            ["format"] = "binary",
            ["path"] = path
        });
    }

    private static void AddRemoteRuleSet(JsonArray sets, HashSet<string> seen, string tag, string url)
    {
        if (!seen.Add(tag)) return;

        sets.Add(new JsonObject
        {
            ["type"] = "remote",
            ["tag"] = tag,
            ["format"] = "binary",
            ["url"] = url,
            // Наборы качаются через туннель: raw.githubusercontent.com часто недоступен напрямую.
            // Результат кладётся в cache_file, так что скачивание разовое.
            ["download_detour"] = ProxyTag,
            ["update_interval"] = "7d"
        });
    }

    // ===================== dns =====================

    /// <summary>
    /// Домены, которые ходят напрямую, и резолвиться должны локально.
    /// Иначе запрос уходит через туннель, сайт с геобалансировкой отдаёт адрес
    /// ближайшего к выходной ноде узла - и «прямое» соединение приезжает
    /// на зарубежный сервер. Заодно это утечка: провайдер прокси иначе
    /// видит все DNS-запросы, включая к прямым сайтам.
    /// </summary>
    private static JsonObject BuildDns(
        List<RouteRule> rules, AppSettings s, JsonArray ruleSets, HashSet<string> seen)
    {
        var dnsRules = new JsonArray();

        foreach (var rule in rules.Where(r => r.Action == RouteAction.Direct))
        {
            var node = ToDnsRule(rule, ruleSets, seen);
            if (node is not null) dnsRules.Add(node);
        }

        return new JsonObject
        {
            ["servers"] = new JsonArray
            {
                RemoteDnsServer(s),
                new JsonObject
                {
                    ["tag"] = "dns-local",
                    ["type"] = "local"
                }
            },
            ["rules"] = dnsRules,
            ["final"] = "dns-remote",
            ["strategy"] = "prefer_ipv4",
            ["independent_cache"] = true
        };
    }

    /// <summary>
    /// Транспорт удалённого резолвера выводится из схемы адреса. По умолчанию DoH:
    /// он идёт по 443 порту и проходит везде, тогда как DoT (853) через прокси
    /// часто висит до таймаута - провайдеры и сами прокси-серверы его режут.
    ///
    ///   https://1.1.1.1/dns-query  - DoH, значение по умолчанию
    ///   tls://1.1.1.1              - DoT
    ///   quic://1.1.1.1             - DoQ
    ///   udp://1.1.1.1              - обычный DNS, шифрования нет
    /// </summary>
    private static JsonObject RemoteDnsServer(AppSettings s)
    {
        var raw = s.RemoteDns.Trim();
        var type = "https";
        var host = raw;
        var path = "/dns-query";
        var port = 0;

        if (raw.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            host = uri.Host;

            type = uri.Scheme.ToLowerInvariant() switch
            {
                "tls" or "dot" => "tls",
                "quic" or "doq" => "quic",
                "h3" => "h3",
                "udp" or "dns" => "udp",
                "tcp" => "tcp",
                _ => "https"
            };

            if (uri.AbsolutePath.Length > 1) path = uri.AbsolutePath;
            if (!uri.IsDefaultPort) port = uri.Port;
        }

        var node = new JsonObject
        {
            ["tag"] = "dns-remote",
            ["type"] = type,
            ["server"] = host,
            ["detour"] = ProxyTag
        };

        if (type is "https" or "h3") node["path"] = path;
        if (port > 0) node["server_port"] = port;

        return node;
    }

    /// <summary>
    /// В DNS-правило можно перенести только доменные условия: на момент запроса
    /// адрес ещё неизвестен, поэтому geoip и process_name здесь бессмысленны.
    /// </summary>
    private static JsonObject? ToDnsRule(RouteRule rule, JsonArray ruleSets, HashSet<string> seen)
    {
        var value = rule.Value.Trim();
        if (value.Length == 0) return null;

        var node = new JsonObject();

        switch (rule.Match)
        {
            case MatchKind.Geosite:
            {
                var tag = $"geosite-{value}";
                AddRemoteRuleSet(ruleSets, seen, tag, $"{GeositeBase}/{tag}.srs");
                node["rule_set"] = new JsonArray { tag };
                break;
            }
            case MatchKind.Domain:
                node["domain"] = new JsonArray { value };
                break;
            case MatchKind.DomainSuffix:
                node["domain_suffix"] = new JsonArray { value };
                break;
            case MatchKind.DomainKeyword:
                node["domain_keyword"] = new JsonArray { value };
                break;
            case MatchKind.RuleSetRemote:
            {
                var tag = SetTag(value);
                AddRemoteRuleSet(ruleSets, seen, tag, value);
                node["rule_set"] = new JsonArray { tag };
                break;
            }
            case MatchKind.RuleSetLocal:
            {
                var tag = SetTag(value);
                AddLocalRuleSet(ruleSets, seen, tag, value);
                node["rule_set"] = new JsonArray { tag };
                break;
            }
            default:
                return null;
        }

        node["server"] = "dns-local";
        return node;
    }
}