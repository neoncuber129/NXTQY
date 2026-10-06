using Inventory.Domain.Models;
using Inventory.Domain.Services;
using Inventory.App.Helpers;
using Xunit;

namespace Inventory.Tests;

public class ItemVariantKeyTests
{
    [Fact]
    public void SameNamePrice_DifferentUsage_AreDifferentKeys()
    {
        var a = ItemVariantKey.Build("Giấy A4", 10000, "VP", "trên cấp");
        var b = ItemVariantKey.Build("Giấy A4", 10000, "VP", "");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Matches_RequiresAllFourFields()
    {
        var item = new Item
        {
            ItemCode = "A01",
            ItemName = "Giấy A4",
            UnitPrice = 10000,
            Category = "VP",
            UsageType = "trên cấp"
        };

        Assert.True(ItemVariantKey.Matches(item, "Giấy A4", 10000, "VP", "trên cấp"));
        Assert.False(ItemVariantKey.Matches(item, "Giấy A4", 10000, "VP", ""));
        Assert.False(ItemVariantKey.Matches(item, "Giấy A4", 10000, "YT", "trên cấp"));
    }

    [Fact]
    public void Resolver_ExactMatch_WhenFourKeyMatches()
    {
        var catalog = new[]
        {
            new Item { ItemCode = "A01", ItemName = "Giấy A4", UnitPrice = 10000, Category = "VP", UsageType = "trên cấp" },
            new Item { ItemCode = "A02", ItemName = "Giấy A4", UnitPrice = 10000, Category = "VP", UsageType = "" }
        };

        var r = ItemVariantResolver.Resolve(catalog, null, "Giấy A4", 10000, "VP", "trên cấp");
        Assert.Equal(ItemVariantResolutionKind.ExactMatch, r.Kind);
        Assert.Equal("A01", r.ExactItem!.ItemCode);
    }

    [Fact]
    public void Resolver_Ambiguous_WhenNamePriceMatchButUsageDiffers()
    {
        var catalog = new[]
        {
            new Item { ItemCode = "A01", ItemName = "Giấy A4", UnitPrice = 10000, Category = "VP", UsageType = "trên cấp" },
            new Item { ItemCode = "A02", ItemName = "Giấy A4", UnitPrice = 10000, Category = "VP", UsageType = "" }
        };

        var r = ItemVariantResolver.Resolve(catalog, null, "Giấy A4", 10000, "YT", "");
        Assert.Equal(ItemVariantResolutionKind.Ambiguous, r.Kind);
        Assert.Equal(2, r.Candidates.Count);
    }

    [Fact]
    public void Resolver_ForceCreateNew_SkipsAmbiguity()
    {
        var catalog = new[]
        {
            new Item { ItemCode = "A01", ItemName = "Giấy A4", UnitPrice = 10000, Category = "VP", UsageType = "trên cấp" }
        };

        var r = ItemVariantResolver.Resolve(catalog, null, "Giấy A4", 10000, "YT", "", forceCreateNewVariant: true);
        Assert.Equal(ItemVariantResolutionKind.NewItem, r.Kind);
    }

    [Fact]
    public void Resolver_BoundByCode_WhenMetadataMatches()
    {
        var catalog = new[]
        {
            new Item { ItemCode = "A01", ItemName = "Giấy A4", UnitPrice = 10000, Category = "VP", UsageType = "trên cấp" },
            new Item { ItemCode = "A02", ItemName = "Giấy A4", UnitPrice = 10000, Category = "YT", UsageType = "" }
        };

        var r = ItemVariantResolver.Resolve(catalog, "A02", "Giấy A4", 10000, "YT", "");
        Assert.Equal(ItemVariantResolutionKind.BoundByCode, r.Kind);
        Assert.Equal("A02", r.ExactItem!.ItemCode);
    }

    [Fact]
    public void Resolver_Ambiguous_WhenCodeMatchesButMetadataDiffers()
    {
        var catalog = new[]
        {
            new Item { ItemCode = "A01", ItemName = "Giấy A4", UnitPrice = 10000, Category = "VP", UsageType = "trên cấp" }
        };

        var r = ItemVariantResolver.Resolve(catalog, "A01", "Giấy A4", 10000, "YT", "");
        Assert.Equal(ItemVariantResolutionKind.Ambiguous, r.Kind);
        Assert.Contains(r.Candidates, c => c.ItemCode == "A01");
    }

    [Fact]
    public void BuildBaseItemCode_IsNameColonPrice()
    {
        Assert.Equal("Giấy A4:10000", ItemVariantKey.BuildBaseItemCode("Giấy A4", 10000));
    }

    [Fact]
    public void BuildVariantItemCode_AddsCategoryAndTcTags()
    {
        Assert.Equal("Giấy A4:10000|VP|TC", ItemVariantKey.BuildVariantItemCode("Giấy A4", 10000, "VP", "trên cấp"));
        Assert.Equal("Giấy A4:10000|TC", ItemVariantKey.BuildVariantItemCode("Giấy A4", 10000, "", "trên cấp"));
        Assert.Equal("Giấy A4:10000|VP", ItemVariantKey.BuildVariantItemCode("Giấy A4", 10000, "VP", ""));
        Assert.Null(ItemVariantKey.BuildVariantItemCode("Giấy A4", 10000, "", ""));
    }

    [Fact]
    public void DuplicateUnitPriceChecker_DetectsDuplicatesInDocumentAndDashboard()
    {
        var docLines = new[]
        {
            new DuplicateUnitPriceChecker.ItemLineInput(1, "Mặt hàng A", "A01", 50000),
            new DuplicateUnitPriceChecker.ItemLineInput(2, "Mặt hàng B", "B01", 50000),
            new DuplicateUnitPriceChecker.ItemLineInput(3, "Mặt hàng C", "C01", 100000)
        };

        var catalog = new[]
        {
            new Item { ItemCode = "D01", ItemName = "Mặt hàng D", UnitPrice = 100000, Category = "YT" }
        };

        var groups = DuplicateUnitPriceChecker.FindDuplicateGroups(docLines, catalog);

        Assert.Equal(2, groups.Count);

        var group50k = groups.First(g => g.UnitPrice == 50000);
        Assert.Equal(2, group50k.InDocumentItems.Count);
        Assert.Empty(group50k.DashboardItems);

        var group100k = groups.First(g => g.UnitPrice == 100000);
        Assert.Single(group100k.InDocumentItems);
        Assert.Single(group100k.DashboardItems);
    }
}
