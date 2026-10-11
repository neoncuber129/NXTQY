using Inventory.App;
using Inventory.App.Helpers;
using Inventory.App.Views;
using Inventory.Domain.Models;
using Inventory.Reporting.Services;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Tests;

public sealed class SettingsAndViewModelTests
{
    [Theory]
    [InlineData("yes")]
    [InlineData("YES")]
    [InlineData(" Yes ")]
    public void Typed_confirmation_accepts_yes_ignoring_case_and_outer_spaces(string value)
    {
        Assert.True(TypedConfirmationWindow.IsConfirmationTextValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("y")]
    [InlineData("yes please")]
    [InlineData("no")]
    public void Typed_confirmation_rejects_any_text_other_than_yes(string? value)
    {
        Assert.False(TypedConfirmationWindow.IsConfirmationTextValid(value));
    }

    [Fact]
    public void Template_folder_round_trip_uses_fixed_file_names_and_survives_bad_json()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var templateFolder = Path.Combine(directory.Path, "Mẫu Unicode");
        var service = new WordTemplateSettingsService(settingsPath);

        service.SaveFolder($"  {templateFolder}  ");
        var loaded = service.Load();

        Assert.Equal(templateFolder, loaded.TemplateFolderPath);
        Assert.Equal(Path.Combine(templateFolder, "Phieunhap.docx"), loaded.ReceiptTemplatePath);
        Assert.Equal(Path.Combine(templateFolder, "Phieuxuat.docx"), loaded.IssueTemplatePath);
        Assert.Equal(Path.Combine(templateFolder, "Phieucandoi.docx"), loaded.BalanceTemplatePath);

        File.WriteAllText(settingsPath, "{ not valid json");
        loaded = service.Load();
        Assert.Equal(string.Empty, loaded.TemplateFolderPath);
        Assert.Equal(string.Empty, loaded.ReceiptTemplatePath);

        service.SaveFolder(templateFolder);
        service.Reset();
        Assert.False(File.Exists(settingsPath));
        Assert.Equal(string.Empty, service.Load().TemplateFolderPath);
    }

    [Fact]
    public void Unit_header_preference_round_trips_without_using_real_local_app_data()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "ui.json");

        Assert.True(UiPreferenceService.LoadShowBalanceUnitCodes(settingsPath));
        UiPreferenceService.SaveShowBalanceUnitCodes(true, settingsPath);
        Assert.True(UiPreferenceService.LoadShowBalanceUnitCodes(settingsPath));
        UiPreferenceService.SaveShowBalanceUnitCodes(false, settingsPath);
        Assert.False(UiPreferenceService.LoadShowBalanceUnitCodes(settingsPath));

        File.WriteAllText(settingsPath, "bad json");
        Assert.True(UiPreferenceService.LoadShowBalanceUnitCodes(settingsPath));
        UiPreferenceService.Reset(settingsPath);
        Assert.False(File.Exists(settingsPath));
    }

    [Fact]
    public void Unit_allocations_drive_total_quantity_and_output_dictionary()
    {
        var line = new LineInputViewModel();
        line.InitializeUnitAllocations(
        [
            new SubordinateUnit { Id = 2, Name = "B", SortOrder = 2 },
            new SubordinateUnit { Id = 1, Name = "A", SortOrder = 1 }
        ]);

        Assert.Equal([1, 2], line.UnitAllocations.Select(x => x.UnitId).ToArray());
        line.UnitAllocations[0].Quantity = 3;
        line.UnitAllocations[1].Quantity = 7;
        Assert.Equal(10, line.Quantity);
        var allocations = line.GetAllocationsDictionary();
        Assert.Equal(2, allocations.Count);
        Assert.Equal(3, allocations[1]);
        Assert.Equal(7, allocations[2]);

        line.UnitAllocations[0].Quantity = 0;
        Assert.Equal(7, line.Quantity);
        Assert.DoesNotContain(1, line.GetAllocationsDictionary().Keys);
    }

    [Fact]
    public void Line_marks_insufficient_stock_as_soon_as_remaining_quantity_is_negative()
    {
        var line = new LineInputViewModel
        {
            StockAtIssueDateQuantity = 5,
            Quantity = 7,
            AvailableQuantity = -2
        };

        Assert.True(line.HasInsufficientStock);
        Assert.Equal(2, line.StockShortageQuantity);

        line.AvailableQuantity = 0;
        Assert.False(line.HasInsufficientStock);
        Assert.Equal(0, line.StockShortageQuantity);
    }

    [Fact]
    public void Receipt_view_model_validates_trims_builds_totals_and_tracks_dirty_state()
    {
        var item = new Item
        {
            Id = 1,
            ItemCode = "MH1",
            ItemName = "Mặt hàng",
            Category = "Thuốc",
            Unit = "Hộp",
            UsageType = "Cấp trên",
            UnitPrice = 2_000
        };
        var viewModel = new ReceiptDialogViewModel([item], existing: null)
        {
            ReceiptNumber = " PN-01 ",
            DeliveredBy = " KH ",
            DeliveredUnit = " HĐ 01 ",
            ReceivedBy = " Kho ",
            Reason = " Nhập "
        };
        viewModel.AddLineCommand.Execute(null);
        var line = Assert.Single(viewModel.Lines);
        line.ItemCode = "MH1";
        line.Quantity = 4;
        line.UnitPrice = 2_000;

        Assert.True(viewModel.Validate(out var message), message);
        Assert.Equal(8_000, viewModel.TotalAmount);
        Assert.True(viewModel.IsDirty);

        var input = viewModel.BuildInput();
        Assert.Equal("PN-01", input.ReceiptNumber);
        Assert.Equal("KH", input.DeliveredBy);
        Assert.Equal("HĐ 01", input.DeliveredUnit);
        Assert.Equal(8_000, input.Lines.Sum(x => x.Quantity * x.UnitPrice!.Value));

        viewModel.AcceptChanges();
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public void Receipt_edit_mode_loads_item_code_name_category_and_unit()
    {
        var item = new Item
        {
            Id = 7,
            ItemCode = "Giay:10000",
            ItemName = "Giấy A4",
            Category = "VP",
            Unit = "Ram",
            UsageType = "trên cấp",
            UnitPrice = 10_000
        };
        var existing = new Receipt
        {
            Id = 1,
            ReceiptNumber = "PN-EDIT",
            ReceiptDate = new DateTime(2026, 7, 24),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new ReceiptLine
                {
                    ItemId = 7,
                    Quantity = 3,
                    UnitPrice = 10_000,
                    Amount = 30_000,
                    Note = "ghi chu"
                }
            ]
        };

        var viewModel = new ReceiptDialogViewModel([item], existing);
        var line = Assert.Single(viewModel.Lines);
        Assert.Equal("Giay:10000", line.ItemCode);
        Assert.Equal("Giấy A4", line.ItemName);
        Assert.Equal("VP", line.Category);
        Assert.Equal("Ram", line.Unit);
        Assert.Equal("trên cấp", line.UsageType);
        Assert.Equal(3m, line.Quantity);
        Assert.Equal(10_000m, line.UnitPrice);
        Assert.True(line.IsCatalogBound);
    }

    [Fact]
    public void Receipt_view_model_removes_all_checked_lines_and_recalculates_once()
    {
        var viewModel = new ReceiptDialogViewModel([], existing: null);
        viewModel.AddLineCommand.Execute(null);
        viewModel.AddLineCommand.Execute(null);
        viewModel.AddLineCommand.Execute(null);

        for (var i = 0; i < viewModel.Lines.Count; i++)
        {
            viewModel.Lines[i].ItemName = $"Hàng {i + 1}";
            viewModel.Lines[i].Quantity = i + 1;
            viewModel.Lines[i].UnitPrice = 100;
        }

        viewModel.Lines[0].IsSelected = true;
        viewModel.Lines[2].IsSelected = true;
        viewModel.RemoveSelectedLinesCommand.Execute(null);

        var remaining = Assert.Single(viewModel.Lines);
        Assert.Equal("Hàng 2", remaining.ItemName);
        Assert.Equal(1, remaining.RowNumber);
        Assert.Equal(200, viewModel.TotalAmount);
    }

    [Fact]
    public void Issue_view_model_keeps_balance_reason_in_dirty_state_and_save_input()
    {
        var viewModel = new IssueDialogViewModel(
            [],
            [],
            [],
            existing: null,
            (_, _) => Task.FromResult<IReadOnlyDictionary<string, decimal>>(
                new Dictionary<string, decimal>()),
            (_, _) => Task.FromResult(0m),
            _ => Task.FromResult<Receipt?>(null));
        viewModel.CaptureBaseline();

        viewModel.Reason = "Cấp hàng theo kế hoạch";

        Assert.True(viewModel.IsDirty);
        Assert.Equal("Cấp hàng theo kế hoạch", viewModel.BuildInput().Reason);
    }

    [Fact]
    public async Task Cancelled_opening_confirmation_keeps_changes_dirty_and_does_not_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var directory = new TemporaryDirectory();
        var viewModel = new MainViewModel(
            database.Service,
            new ExcelImportService(),
            new ExcelTemplateService(),
            new DashboardExcelExportService(),
            new WordExportService(),
            new WordTemplateSettingsService(Path.Combine(directory.Path, "word-settings.json")))
        {
            OpeningBalanceConfirmation = () => false
        };
        viewModel.CaptureOpeningBaseline();
        viewModel.OpeningBalanceLines.Add(new OpeningBalanceInputViewModel
        {
            Category = "Thuốc",
            ItemName = "Mặt hàng xác nhận",
            Unit = "Hộp",
            UnitPrice = 1_000,
            OpeningQuantity = 10
        });

        Assert.True(viewModel.HasUnsavedOpeningBalances);
        await viewModel.SaveOpeningBalancesCommand.ExecuteAsync(null);

        await using var context = database.CreateContext();
        Assert.Equal(0, await context.StockPeriods.CountAsync());
        Assert.True(viewModel.HasUnsavedOpeningBalances);
        Assert.Contains("Đã hủy", viewModel.StatusMessage);
    }

    [Fact]
    public void Open_opening_total_adjust_window_handles_no_valid_lines()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = new MainViewModel(
            null!,
            null!,
            null!,
            null!,
            null!,
            new WordTemplateSettingsService(Path.Combine(directory.Path, "word-settings.json")));

        viewModel.OpenOpeningTotalAdjustWindow();
        Assert.Contains("Không có dòng", viewModel.StatusMessage);
    }

    [Fact]
    public void Duplicate_unit_price_checker_detects_duplicates_in_opening_balance_lines()
    {
        var docLines = new List<DuplicateUnitPriceChecker.ItemLineInput>
        {
            new(1, "Mặt hàng A", string.Empty, 5_000),
            new(2, "Mặt hàng B", string.Empty, 5_000)
        };

        var catalog = new List<Item>();
        var groups = DuplicateUnitPriceChecker.FindDuplicateGroups(docLines, catalog);

        var group = Assert.Single(groups);
        Assert.Equal(5_000m, group.UnitPrice);
        Assert.Equal(2, group.InDocumentItems.Count);
    }

    [Fact]
    public async Task Dashboard_edit_stays_in_memory_until_explicit_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.AddItemAsync(new Item
        {
            ItemCode = "MH-DRAFT",
            ItemName = "Mặt hàng bản nháp",
            Category = "Cũ",
            Unit = "Hộp",
            UsageType = string.Empty,
            UnitPrice = 1_000
        });
        using var directory = new TemporaryDirectory();
        var viewModel = new MainViewModel(
            database.Service,
            new ExcelImportService(),
            new ExcelTemplateService(),
            new DashboardExcelExportService(),
            new WordExportService(),
            new WordTemplateSettingsService(Path.Combine(directory.Path, "word-settings.json")));
        await viewModel.InitializeAsync();

        var row = Assert.Single(viewModel.DashboardRows);
        row.Category = "Mới";
        await viewModel.CommitDashboardRowEditAsync(row);

        Assert.True(viewModel.HasUnsavedDashboardMetadata);
        await using (var beforeSave = database.CreateContext())
        {
            Assert.Equal("Cũ", (await beforeSave.Items.SingleAsync()).Category);
        }

        await viewModel.SaveDashboardChangesCommand.ExecuteAsync(null);

        await using var afterSave = database.CreateContext();
        Assert.Equal("Mới", (await afterSave.Items.SingleAsync()).Category);
        Assert.False(viewModel.HasUnsavedDashboardMetadata);
    }

    [Fact]
    public async Task Unit_reorder_draft_and_persist_only_after_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "A",
            Name = "Đơn vị A"
        });
        await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "B",
            Name = "Đơn vị B"
        });
        using var directory = new TemporaryDirectory();
        var viewModel = new MainViewModel(
            database.Service,
            new ExcelImportService(),
            new ExcelTemplateService(),
            new DashboardExcelExportService(),
            new WordExportService(),
            new WordTemplateSettingsService(Path.Combine(directory.Path, "word-settings.json")));
        await viewModel.InitializeAsync();

        var unitA = viewModel.SubordinateUnits[0];
        viewModel.ReorderSubordinateUnit(unitA, 1);

        Assert.Equal(["B", "A"], viewModel.SubordinateUnits.Select(x => x.UnitCode).ToArray());
        Assert.Equal([1, 2], viewModel.SubordinateUnits.Select(x => x.SortOrder).ToArray());
        Assert.True(viewModel.HasUnsavedSubordinateUnits);
        await using (var beforeSave = database.CreateContext())
        {
            Assert.Equal(
                ["A", "B"],
                (await beforeSave.SubordinateUnits.OrderBy(x => x.SortOrder).ToListAsync())
                .Select(x => x.UnitCode).ToArray());
        }

        await viewModel.SaveSubordinateUnitsCommand.ExecuteAsync(null);
        await using var afterSave = database.CreateContext();
        Assert.Equal(
            ["B", "A"],
            (await afterSave.SubordinateUnits.OrderBy(x => x.SortOrder).ToListAsync())
            .Select(x => x.UnitCode).ToArray());
    }

    [Fact]
    public async Task Managed_unit_issue_edit_stays_draft_until_explicit_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        var unit = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "QLPX",
            Name = "Đơn vị quản lý phiếu"
        });
        int childId;
        await using (var context = database.CreateContext())
        {
            var parent = new Issue
            {
                IssueNumber = "CD-QLPX",
                IssueDate = new DateTime(2026, 5, 1),
                ReceivedBy = string.Empty,
                ReceivedUnit = string.Empty,
                DeliveredBy = string.Empty,
                Reason = string.Empty,
                TotalAmountText = string.Empty
            };
            context.Issues.Add(parent);
            await context.SaveChangesAsync();
            var child = new Issue
            {
                IssueNumber = "PX-QLPX-CU",
                IssueDate = new DateTime(2026, 5, 2),
                ParentIssueId = parent.Id,
                SubordinateUnitId = unit.Id,
                ReceivedUnit = unit.Name,
                ReceivedBy = "Người cũ",
                DeliveredBy = "Kho",
                Reason = "Lý do cũ",
                TotalAmountText = string.Empty,
                Lines = [new IssueLine { ItemId = 1, Quantity = 2, UnitPrice = 1_000, Amount = 2_000 }]
            };
            context.Issues.Add(child);
            await context.SaveChangesAsync();
            childId = child.Id;
        }

        using var directory = new TemporaryDirectory();
        var viewModel = new MainViewModel(
            database.Service,
            new ExcelImportService(),
            new ExcelTemplateService(),
            new DashboardExcelExportService(),
            new WordExportService(),
            new WordTemplateSettingsService(Path.Combine(directory.Path, "word-settings.json")));
        await viewModel.InitializeAsync();
        var row = Assert.Single(viewModel.ManagedUnitIssueRows);

        row.IssueNumber = "PX-QLPX-MOI";
        row.ReceivedBy = "Người mới";
        Assert.True(row.IsDirty);
        Assert.True(viewModel.HasUnsavedManagedUnitIssues);
        Assert.True(viewModel.HasUnsavedMainEdits);
        await using (var beforeSave = database.CreateContext())
        {
            Assert.Equal("PX-QLPX-CU", (await beforeSave.Issues.SingleAsync(x => x.Id == childId)).IssueNumber);
        }

        await viewModel.SaveManagedUnitIssuesCommand.ExecuteAsync(null);

        await using var afterSave = database.CreateContext();
        var saved = await afterSave.Issues.SingleAsync(x => x.Id == childId);
        Assert.Equal("PX-QLPX-MOI", saved.IssueNumber);
        Assert.Equal("Người mới", saved.ReceivedBy);
        Assert.False(viewModel.HasUnsavedManagedUnitIssues);
    }

    [Fact]
    public async Task ItemPickerViewModel_hides_zero_stock_by_default_and_loads_availability()
    {
        var item1 = new Item { Id = 1, ItemCode = "VT01", ItemName = "Vật tư 1", Category = "A", Unit = "Cái", UnitPrice = 1000m, UsageType = "Tiêu hao" };
        var item2 = new Item { Id = 2, ItemCode = "VT02", ItemName = "Vật tư 2", Category = "A", Unit = "Cái", UnitPrice = 2000m, UsageType = "Tiêu hao" };
        var item3 = new Item { Id = 3, ItemCode = "VT03", ItemName = "Vật tư 3", Category = "B", Unit = "Hộp", UnitPrice = 3000m, UsageType = "Tiêu hao" };

        var selectables = new List<IssueSelectableItemViewModel>
        {
            new(item1),
            new(item2),
            new(item3)
        };

        var itemsDict = new Dictionary<int, Item>
        {
            [1] = item1,
            [2] = item2,
            [3] = item3
        };

        var issueDate = new DateTime(2026, 10, 15);
        var stockMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["VT01"] = 10m,
            ["VT02"] = 0m,
            ["VT03"] = -2m
        };

        var vm = new ItemPickerViewModel(
            selectables,
            [],
            itemsDict,
            [],
            issueDate,
            receiptId => Task.FromResult<Receipt?>(null),
            (code, date) => Task.FromResult(stockMap.GetValueOrDefault(code, 0m)),
            (codes, date) => Task.FromResult<IReadOnlyDictionary<string, decimal>>(stockMap));

        Assert.True(vm.HideZeroStock);
        Assert.Contains("15/10/2026", vm.DateNoticeText);

        await vm.LoadAvailabilityAsync();

        Assert.Equal(10m, selectables[0].AvailableStock);
        Assert.Equal(0m, selectables[1].AvailableStock);
        Assert.Equal(-2m, selectables[2].AvailableStock);

        var visibleWhenHidden = vm.FullItemsView.Cast<IssueSelectableItemViewModel>().ToList();
        Assert.Single(visibleWhenHidden);
        Assert.Equal("VT01", visibleWhenHidden[0].ItemCode);

        vm.HideZeroStock = false;
        var visibleWhenShown = vm.FullItemsView.Cast<IssueSelectableItemViewModel>().ToList();
        Assert.Equal(3, visibleWhenShown.Count);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "InventorySettingsTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
