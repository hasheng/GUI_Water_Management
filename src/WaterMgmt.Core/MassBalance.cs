namespace WaterMgmt.Core;

public enum BalanceStatus { Balanced, StorageChange, Imbalance, OverCapacity, OverPermit, Incomplete, Boundary }

public record NodeBalance(SiteFeature Feature, double In, double Out, double OtherIn, double OtherLoss,
    double Net, BalanceStatus Status, string Note);

/// <summary>
/// Steady-state water balance per node from pipe flows (L/min): in + other in - out - other loss = net.
/// Pits/ponds/sumps may store water (net = storage change); pumps, wash stations and "other" must balance.
/// Wells and outfalls are boundaries. Pumps are also checked against rated capacity and permit L/min.
/// </summary>
public static class MassBalance
{
    static bool IsStorage(FeatureKind k) => k is FeatureKind.Pit or FeatureKind.StormPond or FeatureKind.Sump;
    static bool IsBoundary(FeatureKind k) => k is FeatureKind.Well or FeatureKind.Outfall;

    public static List<NodeBalance> Compute(SiteProject p, double tolerancePct = 5)
    {
        var res = new List<NodeBalance>();
        foreach (var f in p.Features)
        {
            var ins = p.Links.Where(l => l.ToId == f.Id).ToList();
            var outs = p.Links.Where(l => l.FromId == f.Id).ToList();
            double fin = ins.Sum(l => l.FlowLpm ?? 0), fout = outs.Sum(l => l.FlowLpm ?? 0);
            double oin = f.OtherInflowLpm ?? 0, oloss = f.OtherLossLpm ?? 0;
            double net = fin + oin - fout - oloss;
            double through = Math.Max(fin + oin, fout + oloss);
            bool missing = ins.Concat(outs).Any(l => l.FlowLpm == null);
            BalanceStatus st; string note = "";

            if (IsBoundary(f.Kind)) { st = BalanceStatus.Boundary; }
            else if (missing) { st = BalanceStatus.Incomplete; note = "A connected pipe has no flow entered."; }
            else if (ins.Count + outs.Count == 0 && oin + oloss == 0) { st = BalanceStatus.Incomplete; note = "Not connected."; }
            else if (Math.Abs(net) <= Math.Max(1, through * tolerancePct / 100)) st = BalanceStatus.Balanced;
            else if (IsStorage(f.Kind)) { st = BalanceStatus.StorageChange; note = net > 0 ? "Filling" : "Draining"; }
            else { st = BalanceStatus.Imbalance; note = net > 0 ? "More water in than out" : "More water out than in"; }

            if (f.Kind == FeatureKind.Pump && !missing)
            {
                if (f.RatedLpm is { } cap && fout > cap * 1.0001)
                { st = BalanceStatus.OverCapacity; note = $"Pumping {fout:N0} L/min exceeds rated {cap:N0}."; }
                var src = p.Permits.SelectMany(pt => pt.Sources).FirstOrDefault(s => s.Id == f.PermitSourceId);
                if (src?.MaxLitresPerMinute is { } lim && fout > lim * 1.0001)
                { st = BalanceStatus.OverPermit; note = $"Pumping {fout:N0} L/min exceeds permit limit {lim:N0} ({src.Name})."; }
            }
            res.Add(new NodeBalance(f, fin, fout, oin, oloss, net, st, note));
        }
        return res;
    }

    public static bool IsProblem(BalanceStatus s) => s is BalanceStatus.Imbalance or BalanceStatus.OverCapacity or BalanceStatus.OverPermit;
}
