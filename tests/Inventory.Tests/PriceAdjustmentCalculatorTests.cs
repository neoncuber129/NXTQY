using Inventory.App.Helpers;
using Xunit;

namespace Inventory.Tests;

public class PriceAdjustmentCalculatorTests
{
    private static PriceAdjustmentCalculator.InputLine Line(int row, decimal qty, decimal price) =>
        new(row, $"Item {row}", qty, price);

    [Fact]
    public void Suggest_SingleLineExact_WhenDeltaDivisibleByQuantity()
    {
        // Tổng hiện tại: 10*1000 + 5*2000 = 20.000; mục tiêu 20.500 → delta 500.
        var lines = new[] { Line(1, 10, 1000), Line(2, 5, 2000) };
        var suggestions = PriceAdjustmentCalculator.Suggest(lines, 20_500);

        Assert.NotEmpty(suggestions);
        var best = suggestions[0];
        Assert.True(best.IsExact);
        Assert.Single(best.Changes);
        // Dòng 1 (qty 10) cho bước giá nhỏ nhất: 1000 → 1050.
        Assert.Equal(1, best.Changes[0].RowNumber);
        Assert.Equal(1050m, best.Changes[0].NewPrice);
        Assert.Equal(20_500m, best.ResultingTotal);
    }

    [Fact]
    public void Suggest_ReturnsEmpty_WhenTargetEqualsCurrent()
    {
        var lines = new[] { Line(1, 2, 500) };
        var suggestions = PriceAdjustmentCalculator.Suggest(lines, 1000);
        Assert.Empty(suggestions);
    }

    [Fact]
    public void Suggest_AllPricesAreWholeNumbers()
    {
        // delta 1 không chia hết cho qty nào → mọi gợi ý vẫn phải là giá nguyên đồng.
        var lines = new[] { Line(1, 3, 100), Line(2, 7, 200) };
        var suggestions = PriceAdjustmentCalculator.Suggest(lines, 1_701);

        Assert.NotEmpty(suggestions);
        Assert.All(suggestions, s => Assert.All(s.Changes, c =>
            Assert.Equal(c.NewPrice, decimal.Truncate(c.NewPrice))));
    }

    [Fact]
    public void Suggest_TwoLineExact_WhenNoSingleLineExact()
    {
        // Tổng hiện tại: 3*100 + 7*200 = 1.700; mục tiêu 1.701 → delta 1.
        // Không dòng nào chia hết, nhưng 3·(-2) + 7·(1) = 1 → chỉnh 2 dòng khớp đúng.
        var lines = new[] { Line(1, 3, 100), Line(2, 7, 200) };
        var suggestions = PriceAdjustmentCalculator.Suggest(lines, 1_701);

        var best = suggestions[0];
        Assert.True(best.IsExact);
        Assert.Equal(2, best.Changes.Count);
        Assert.Equal(1_701m, best.Changes.Sum(c => c.Quantity * c.NewPrice));
    }

    [Fact]
    public void Suggest_NeverProducesNegativePrice()
    {
        // Giảm tổng mạnh: 2*100 = 200 → mục tiêu 10.
        var lines = new[] { Line(1, 2, 100) };
        var suggestions = PriceAdjustmentCalculator.Suggest(lines, 10);

        Assert.All(suggestions, s => Assert.All(s.Changes, c => Assert.True(c.NewPrice >= 0)));
        var exact = suggestions.FirstOrDefault(x => x.IsExact);
        Assert.NotNull(exact);
        Assert.Equal(5m, exact!.Changes[0].NewPrice);
    }

    [Fact]
    public void Suggest_PrefersSmallestPriceChange()
    {
        // delta 100 chia hết cho cả qty 10 (bước 10) và qty 4 (bước 25) → ưu tiên bước 10.
        var lines = new[] { Line(1, 10, 1000), Line(2, 4, 500) };
        var current = 10 * 1000m + 4 * 500m;
        var suggestions = PriceAdjustmentCalculator.Suggest(lines, current + 100);

        Assert.True(suggestions[0].IsExact);
        Assert.Equal(1, suggestions[0].Changes[0].RowNumber);
        Assert.Equal(10m, suggestions[0].Changes[0].PriceDelta);
    }

    [Fact]
    public void Suggest_FallbackDecimalExact_WhenNoIntegerExactMatches()
    {
        // delta 5 với Q1=10, Q2=20 → gcd(10, 20)=10 không chia hết cho 5 → không có nghiệm nguyên đồng.
        // Fallback Lớp 2 chạy: gợi ý giá thập phân chính xác (2000.25đ hoặc 1000.5đ), IsExact = true, Residual = 0.
        var lines = new[] { Line(1, 10, 1000), Line(2, 20, 2000) };
        var current = 10 * 1000m + 20 * 2000m; // 50.000
        var suggestions = PriceAdjustmentCalculator.Suggest(lines, current + 5);

        Assert.NotEmpty(suggestions);
        var best = suggestions[0];
        Assert.True(best.IsExact);
        Assert.Equal(0m, best.Residual);
        // Dòng 2 (qty 20) có PriceDelta = 0.25đ nhỏ hơn Dòng 1 (0.5đ) → ưu tiên dòng 2.
        Assert.Equal(2, best.Changes[0].RowNumber);
        Assert.Equal(2000.25m, best.Changes[0].NewPrice);
        Assert.Equal(current + 5, best.ResultingTotal);
    }

    [Fact]
    public void Suggest_FallbackDecimalExact_WithFractionalDecimal()
    {
        // Q1=3, Delta=1 → 1/3 lẻ vô hạn, nhưng Fallback vẫn đảm bảo IsExact = true và khớp đúng targetTotal.
        var lines = new[] { Line(1, 3, 100) };
        var suggestions = PriceAdjustmentCalculator.Suggest(lines, 301);

        Assert.NotEmpty(suggestions);
        var best = suggestions[0];
        Assert.True(best.IsExact);
        Assert.Equal(0m, best.Residual);
        Assert.Equal(301m, best.ResultingTotal);
    }
}
