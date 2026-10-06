namespace Inventory.App.Helpers;

/// <summary>Một thay đổi đơn giá đề xuất trên một dòng phiếu.</summary>
public sealed record PriceAdjustmentChange(
    int RowNumber,
    string Label,
    decimal Quantity,
    decimal OldPrice,
    decimal NewPrice)
{
    public decimal PriceDelta => NewPrice - OldPrice;
}

/// <summary>Một phương án điều chỉnh đơn giá để đạt tổng tiền mong muốn.</summary>
public sealed record PriceAdjustmentSuggestion(
    IReadOnlyList<PriceAdjustmentChange> Changes,
    decimal ResultingTotal,
    decimal Residual)
{
    public bool IsExact => Math.Abs(Residual) < 0.0001m;
    public string Summary =>
        Changes.Count == 1
            ? $"Chỉnh 1 dòng (dòng {Changes[0].RowNumber})"
            : $"Chỉnh {Changes.Count} dòng";
}

/// <summary>
/// Gợi ý cách chỉnh đơn giá (không đổi số lượng) để tổng tiền khớp giá trị mong muốn.
/// Chỉ dùng đơn giá nguyên đồng (không thập phân).
/// Ưu tiên phương án chạm đúng tổng với thay đổi đơn giá nhỏ nhất.
/// </summary>
public static class PriceAdjustmentCalculator
{
    public sealed record InputLine(int RowNumber, string Label, decimal Quantity, decimal Price);

    public static List<PriceAdjustmentSuggestion> Suggest(
        IReadOnlyList<InputLine> lines,
        decimal targetTotal,
        int maxSuggestions = 6)
    {
        var suggestions = new List<PriceAdjustmentSuggestion>();
        if (lines.Count == 0 || targetTotal < 0)
        {
            return suggestions;
        }

        var currentTotal = lines.Sum(x => x.Quantity * x.Price);
        var delta = targetTotal - currentTotal;
        if (delta == 0)
        {
            return suggestions;
        }

        // Phương án chỉnh 1 dòng: đơn giá mới = giá cũ + delta / số lượng (làm tròn nguyên đồng).
        foreach (var line in lines.Where(x => x.Quantity > 0))
        {
            var newPrice = decimal.Round(line.Price + delta / line.Quantity, 0, MidpointRounding.AwayFromZero);
            if (newPrice < 0)
            {
                newPrice = 0;
            }

            if (newPrice == line.Price)
            {
                continue;
            }

            var resultingTotal = currentTotal + line.Quantity * (newPrice - line.Price);
            suggestions.Add(new PriceAdjustmentSuggestion(
                [new PriceAdjustmentChange(line.RowNumber, line.Label, line.Quantity, line.Price, newPrice)],
                resultingTotal,
                targetTotal - resultingTotal));
        }

        // Nếu chưa có phương án 1 dòng nào khớp đúng → thử chỉnh 2 dòng nguyên đồng để khớp tuyệt đối.
        if (!suggestions.Any(x => x.IsExact))
        {
            suggestions.AddRange(BuildTwoLineExact(lines, currentTotal, targetTotal));
        }

        // Vẫn chưa khớp đúng → thử phương án nhiều dòng nguyên đồng (tham lam).
        if (!suggestions.Any(x => x.IsExact))
        {
            var combo = BuildGreedyCombo(lines, currentTotal, targetTotal);
            if (combo is not null)
            {
                suggestions.Add(combo);
            }
        }

        // Dự phòng (Fallback): Nếu vẫn chưa có phương án khớp đúng 100% (IsExact) → gợi ý đơn giá thập phân chính xác.
        if (!suggestions.Any(x => x.IsExact))
        {
            suggestions.AddRange(BuildDecimalExactFallback(lines, currentTotal, targetTotal));
        }

        return suggestions
            .OrderByDescending(x => x.IsExact)
            .ThenBy(x => Math.Abs(x.Residual))
            .ThenBy(x => x.Changes.Sum(c => Math.Abs(c.PriceDelta)))
            .ThenBy(x => x.Changes.Count)
            .Take(maxSuggestions)
            .ToList();
    }

    /// <summary>
    /// Tìm phương án chỉnh đúng 2 dòng với bước giá nguyên đồng khớp tuyệt đối tổng:
    /// giải q1·x + q2·y = delta (phương trình Diophantine) và chọn nghiệm có |x| + |y| nhỏ nhất.
    /// Chỉ áp dụng khi số lượng và delta là số nguyên.
    /// </summary>
    private static List<PriceAdjustmentSuggestion> BuildTwoLineExact(
        IReadOnlyList<InputLine> lines,
        decimal currentTotal,
        decimal targetTotal)
    {
        var results = new List<PriceAdjustmentSuggestion>();
        var delta = targetTotal - currentTotal;
        if (delta != decimal.Truncate(delta))
        {
            return results;
        }

        var d = (long)delta;
        var candidates = lines
            .Where(x => x.Quantity > 0 && x.Quantity == decimal.Truncate(x.Quantity))
            .ToList();

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var a = candidates[i];
                var b = candidates[j];
                var q1 = (long)a.Quantity;
                var q2 = (long)b.Quantity;
                var g = Gcd(q1, q2);
                if (d % g != 0)
                {
                    continue;
                }

                // Nghiệm cơ sở từ Euclid mở rộng, rồi dịch t để |x| + |y| nhỏ nhất.
                var (x0, y0) = ExtendedGcd(q1, q2);
                var scale = d / g;
                var baseX = x0 * scale;
                var baseY = y0 * scale;
                var stepX = q2 / g;
                var stepY = q1 / g;

                var tCenter = (long)Math.Round((double)-baseX / stepX);
                PriceAdjustmentSuggestion? best = null;
                for (var t = tCenter - 2; t <= tCenter + 2; t++)
                {
                    var x = baseX + stepX * t;
                    var y = baseY - stepY * t;
                    if (x == 0 || y == 0)
                    {
                        continue; // thực chất là phương án 1 dòng, đã xét ở trên.
                    }

                    var newPriceA = a.Price + x;
                    var newPriceB = b.Price + y;
                    if (newPriceA < 0 || newPriceB < 0)
                    {
                        continue;
                    }

                    var candidate = new PriceAdjustmentSuggestion(
                        [
                            new PriceAdjustmentChange(a.RowNumber, a.Label, a.Quantity, a.Price, newPriceA),
                            new PriceAdjustmentChange(b.RowNumber, b.Label, b.Quantity, b.Price, newPriceB)
                        ],
                        targetTotal,
                        0m);
                    if (best is null
                        || candidate.Changes.Sum(c => Math.Abs(c.PriceDelta)) < best.Changes.Sum(c => Math.Abs(c.PriceDelta)))
                    {
                        best = candidate;
                    }
                }

                if (best is not null)
                {
                    results.Add(best);
                }
            }
        }

        return results
            .OrderBy(x => x.Changes.Sum(c => Math.Abs(c.PriceDelta)))
            .Take(3)
            .ToList();
    }

    /// <summary>
    /// Chỉnh dần từ dòng có số lượng lớn nhất (bước giá nguyên đồng nhỏ nhất),
    /// phần dư dồn về các dòng có số lượng nhỏ. Có thể còn sai lệch nếu không chia hết.
    /// </summary>
    private static PriceAdjustmentSuggestion? BuildGreedyCombo(
        IReadOnlyList<InputLine> lines,
        decimal currentTotal,
        decimal targetTotal)
    {
        var remaining = targetTotal - currentTotal;
        var changes = new List<PriceAdjustmentChange>();

        foreach (var line in lines.Where(x => x.Quantity > 0).OrderByDescending(x => x.Quantity))
        {
            if (remaining == 0)
            {
                break;
            }

            var step = decimal.Truncate(remaining / line.Quantity);
            if (step == 0)
            {
                continue;
            }

            var newPrice = line.Price + step;
            if (newPrice < 0)
            {
                step = -line.Price;
                newPrice = 0;
                if (step == 0)
                {
                    continue;
                }
            }

            changes.Add(new PriceAdjustmentChange(line.RowNumber, line.Label, line.Quantity, line.Price, newPrice));
            remaining -= line.Quantity * step;
        }

        if (changes.Count == 0)
        {
            return null;
        }

        var resultingTotal = targetTotal - remaining;
        return new PriceAdjustmentSuggestion(changes, resultingTotal, remaining);
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return Math.Abs(a);
    }

    /// <summary>Trả về (x, y) sao cho a·x + b·y = gcd(a, b).</summary>
    private static (long X, long Y) ExtendedGcd(long a, long b)
    {
        long x0 = 1, y0 = 0, x1 = 0, y1 = 1;
        while (b != 0)
        {
            var q = a / b;
            (a, b) = (b, a - q * b);
            (x0, x1) = (x1, x0 - q * x1);
            (y0, y1) = (y1, y0 - q * y1);
        }

        return (x0, y0);
    }

    /// <summary>
    /// Phương án dự phòng (Backup Layer): Tính đơn giá có chữ số thập phân chính xác cho từng dòng
    /// sao cho tổng tiền đạt đúng 100% targetTotal.
    /// </summary>
    private static List<PriceAdjustmentSuggestion> BuildDecimalExactFallback(
        IReadOnlyList<InputLine> lines,
        decimal currentTotal,
        decimal targetTotal)
    {
        var results = new List<PriceAdjustmentSuggestion>();
        var delta = targetTotal - currentTotal;

        foreach (var line in lines.Where(x => x.Quantity > 0))
        {
            var newPrice = line.Price + delta / line.Quantity;
            if (newPrice < 0 || newPrice == line.Price)
            {
                continue;
            }

            results.Add(new PriceAdjustmentSuggestion(
                [new PriceAdjustmentChange(line.RowNumber, line.Label, line.Quantity, line.Price, newPrice)],
                targetTotal,
                0m));
        }

        return results
            .OrderBy(x => Math.Abs(x.Changes[0].PriceDelta))
            .Take(3)
            .ToList();
    }
}
