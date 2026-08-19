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
///  1. geosite/geoip удалены - вместо них удалённые rule-set в формате .srs;
///  2. спецвыходы block/dns устарели - вместо них действия правил reject/hijack-dns;
///  3. legacy-поля inbound (sniff, domain_strategy) заменены действием sniff в маршрутах;
///  4. DNS-серверы описываются новым форматом с полями type/server.
/// </summary>
public static class ConfigBuilder
{
    public const string ProxyTag = "proxy";
    public const string DirectTag = "direct";
 
    private const string GeositeBase = "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set";
    private const string GeoipBase = "https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set";
 
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
 
    public static string Build(ServerProfile profile, IEnumerable<RouteRule> rules, AppSettings settings)
    {
        var active = rules.Where(r => r.Enabled).ToList();
        var ruleSets = new JsonArray();
        var seenSets = new HashSet<string>(StringComparer.Ordinal);
 
        var root = new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["level"] = settings.LogLevel,
                ["timestamp"] = true
            },
            ["dns"] = BuildDns(settings),
            ["inbounds"] = BuildInbounds(settings),
            ["outbounds"] = new JsonArray
            {
                BuildOutbound(profile),
                new JsonObject { ["type"] = "direct", ["tag"] = DirectTag }
            },
            ["route"] = BuildRoute(active, settings, ruleSets, seenSets),
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
 
    public static string Write(ServerProfile profile, IEnumerable<RouteRule> rules, AppSettings settings)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.GeneratedConfig, Build(profile, rules, settings));
        return AppPaths.GeneratedConfig;
    }
 
    // ===================== outbound =====================
 
    /// <summary>
    /// Адрес самого прокси-сервера обязан резолвиться мимо туннеля, иначе получается
    /// замкнутый круг. В 1.12+ это делается полем domain_resolver на outbound'е —
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
            ["tag"] = ProxyTag,
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
        ["tag"] = ProxyTag,
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
        ["tag"] = ProxyTag,
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
 
        // reject — действие правила: спецвыход block удалён из ядра
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
 
    private static JsonObject BuildDns(AppSettings s) => new()
    {
        ["servers"] = new JsonArray
        {
            new JsonObject
            {
                ["tag"] = "dns-remote",
                ["type"] = "tls",
                ["server"] = s.RemoteDns,
                ["detour"] = ProxyTag
            },
            // type "local" берёт системные резолверы — работает и в отеле, и за корпоративным NAT
            new JsonObject
            {
                ["tag"] = "dns-local",
                ["type"] = "local"
            }
        },
        ["final"] = "dns-remote",
        ["strategy"] = "prefer_ipv4",
        ["independent_cache"] = true
    };
}