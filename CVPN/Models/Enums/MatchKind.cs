namespace CVPN.Models.Enums;

public enum MatchKind
{
    Geosite,
    Geoip,
    Domain,
    DomainSuffix,
    DomainKeyword,
    Process,
    
    /// <summary>Свой удалённый .srs по ссылке.</summary>
    RuleSetRemote,
 
    /// <summary>Свой .srs с диска.</summary>
    RuleSetLocal
}