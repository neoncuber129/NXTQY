using Inventory.Domain.Models;
using Inventory.Domain.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Inventory.Data.Services;

public sealed class InventoryService(IDbContextFactory<InventoryDbContext> dbContextFactory)
{
    /// <summary>Tăng khi cần chạy lại reconcile tồn một lần sau nâng cấp.</summary>
    public const int IssuedStockReconcileVersion = 1;

    public async Task InitializeAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        // Vá DB cũ: migration đã ghi lịch sử nhưng cột thực tế thiếu (hoặc migration tay chưa được apply).
        await EnsureSchemaPatchesAsync(db);
        await ApplySqlitePragmasAsync(db);

        var currentVersion = await GetMetaIntAsync(db, "IssuedStockReconcileVersion");
        if (currentVersion < IssuedStockReconcileVersion)
        {
            await ReconcileIssuedStockFromBalanceIssuesAsync();
            await SetMetaIntAsync(db, "IssuedStockReconcileVersion", IssuedStockReconcileVersion);
            await db.SaveChangesAsync();
        }
    }

    private static async Task ApplySqlitePragmasAsync(InventoryDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
    }

    /// <summary>
    /// Thêm cột còn thiếu trên SQLite mà không mất dữ liệu.
    /// Dùng khi __EFMigrationsHistory đã có bản ghi nhưng ALTER chưa thực sự chạy.
    /// </summary>
    private static async Task EnsureSchemaPatchesAsync(InventoryDbContext db)
    {
        if (!await ColumnExistsAsync(db, "Items", "NoteManualAt"))
        {
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Items" ADD COLUMN "NoteManualAt" TEXT NULL""");
        }

        await EnsureMigrationHistoryAsync(db, "20260725123000_AddItemNoteManualAt");

        if (!await ColumnExistsAsync(db, "StockPeriods", "Note"))
        {
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "StockPeriods" ADD COLUMN "Note" TEXT NULL""");
        }

        await EnsureMigrationHistoryAsync(db, "20260725120000_AddStockPeriodNote");
    }

    private static async Task<bool> ColumnExistsAsync(InventoryDbContext db, string table, string column)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        // Tên bảng chỉ đến từ hằng số nội bộ (Items / StockPeriods), không từ input người dùng.
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task EnsureMigrationHistoryAsync(InventoryDbContext db, string migrationId)
    {
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ({migrationId}, '8.0.11');
            """);
    }

    private static async Task<int> GetMetaIntAsync(InventoryDbContext db, string key)
    {
        var row = await db.AppMeta.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key);
        return row is not null && int.TryParse(row.Value, out var v) ? v : 0;
    }

    private static async Task SetMetaIntAsync(InventoryDbContext db, string key, int value)
    {
        var row = await db.AppMeta.SingleOrDefaultAsync(x => x.Key == key);
        if (row is null)
        {
            db.AppMeta.Add(new AppMeta { Key = key, Value = value.ToString(CultureInfo.InvariantCulture) });
        }
        else
        {
            row.Value = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    // --- Subordinate Unit CRUD ---

    public async Task<List<SubordinateUnit>> GetSubordinateUnitsAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.SubordinateUnits.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync();
    }

    public async Task<SubordinateUnit> AddSubordinateUnitAsync(SubordinateUnit input)
    {
        if (string.IsNullOrWhiteSpace(input.UnitCode))
        {
            throw new InvalidOperationException("Mã đơn vị trực thuộc không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new InvalidOperationException("Tên đơn vị trực thuộc không được để trống.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var code = input.UnitCode.Trim();
        var normalizedCode = code.ToUpperInvariant();
        var name = input.Name.Trim();
        if (await db.SubordinateUnits.AnyAsync(x => x.UnitCode.ToUpper() == normalizedCode))
        {
            throw new InvalidOperationException($"Mã đơn vị '{code}' đã tồn tại.");
        }

        if (await db.SubordinateUnits.AnyAsync(x => x.Name == name))
        {
            throw new InvalidOperationException($"Đơn vị trực thuộc '{name}' đã tồn tại.");
        }

        var maxOrder = await db.SubordinateUnits.MaxAsync(x => (int?)x.SortOrder) ?? 0;
        var unit = new SubordinateUnit
        {
            UnitCode = code,
            Name = name,
            SortOrder = input.SortOrder > 0 ? input.SortOrder : maxOrder + 1,
            ReceivedBy = input.ReceivedBy?.Trim() ?? string.Empty,
            DeliveredBy = input.DeliveredBy?.Trim() ?? string.Empty,
            Reason = input.Reason?.Trim() ?? string.Empty,
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim()
        };
        db.SubordinateUnits.Add(unit);
        await db.SaveChangesAsync();
        return unit;
    }

    public async Task UpdateSubordinateUnitAsync(SubordinateUnit input)
    {
        if (string.IsNullOrWhiteSpace(input.UnitCode))
        {
            throw new InvalidOperationException("Mã đơn vị trực thuộc không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new InvalidOperationException("Tên đơn vị trực thuộc không được để trống.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var unit = await db.SubordinateUnits.SingleOrDefaultAsync(x => x.Id == input.Id)
            ?? throw new InvalidOperationException("Không tìm thấy đơn vị trực thuộc.");

        var code = input.UnitCode.Trim();
        var normalizedCode = code.ToUpperInvariant();
        var name = input.Name.Trim();
        if (await db.SubordinateUnits.AnyAsync(x => x.Id != input.Id && x.UnitCode.ToUpper() == normalizedCode))
        {
            throw new InvalidOperationException($"Mã đơn vị '{code}' đã tồn tại.");
        }

        if (await db.SubordinateUnits.AnyAsync(x => x.Id != input.Id && x.Name == name))
        {
            throw new InvalidOperationException($"Đơn vị trực thuộc '{name}' đã tồn tại.");
        }

        unit.UnitCode = code;
        unit.Name = name;
        unit.SortOrder = input.SortOrder;
        unit.ReceivedBy = input.ReceivedBy?.Trim() ?? string.Empty;
        unit.DeliveredBy = input.DeliveredBy?.Trim() ?? string.Empty;
        unit.Reason = input.Reason?.Trim() ?? string.Empty;
        unit.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        await db.SaveChangesAsync();
    }

    public async Task SaveSubordinateUnitsAsync(IReadOnlyCollection<SubordinateUnit> units)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var ids = units.Select(x => x.Id).ToList();
        var dbUnits = await db.SubordinateUnits.Where(x => ids.Contains(x.Id)).ToListAsync();
        var byId = dbUnits.ToDictionary(x => x.Id);

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in units)
        {
            if (string.IsNullOrWhiteSpace(input.UnitCode))
            {
                throw new InvalidOperationException("Mã đơn vị trực thuộc không được để trống.");
            }

            if (string.IsNullOrWhiteSpace(input.Name))
            {
                throw new InvalidOperationException("Tên đơn vị trực thuộc không được để trống.");
            }

            var code = input.UnitCode.Trim();
            var name = input.Name.Trim();
            if (!codes.Add(code))
            {
                throw new InvalidOperationException($"Mã đơn vị bị trùng trong danh sách: {code}.");
            }

            if (!names.Add(name))
            {
                throw new InvalidOperationException($"Tên đơn vị bị trùng trong danh sách: {name}.");
            }

            if (!byId.TryGetValue(input.Id, out var unit))
            {
                throw new InvalidOperationException($"Không tìm thấy đơn vị Id={input.Id}.");
            }

            unit.UnitCode = code;
            unit.Name = name;
            unit.SortOrder = input.SortOrder;
            unit.ReceivedBy = input.ReceivedBy?.Trim() ?? string.Empty;
            unit.DeliveredBy = input.DeliveredBy?.Trim() ?? string.Empty;
            unit.Reason = input.Reason?.Trim() ?? string.Empty;
            unit.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteSubordinateUnitAsync(int id)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var unit = await db.SubordinateUnits.SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Không tìm thấy đơn vị trực thuộc.");

        var hasAllocations = await db.IssueLineAllocations.AnyAsync(x => x.SubordinateUnitId == id);
        if (hasAllocations)
        {
            throw new InvalidOperationException("Không thể xóa đơn vị trực thuộc đang được sử dụng trong phiếu xuất.");
        }

        db.SubordinateUnits.Remove(unit);
        await db.SaveChangesAsync();
    }

    public async Task<List<Item>> GetItemsAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.Items.AsNoTracking().OrderBy(x => x.ItemCode).ToListAsync();
    }

    public async Task ClearAllDataAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        await db.IssueLineAllocations.ExecuteDeleteAsync();
        await db.StockLedgerEntries.ExecuteDeleteAsync();
        await db.ReceiptLines.ExecuteDeleteAsync();
        await db.IssueLines.ExecuteDeleteAsync();
        await db.Receipts.ExecuteDeleteAsync();
        // Issues có khóa ngoại tự tham chiếu ParentIssueId với DeleteBehavior.Restrict.
        // Phải xóa các phiếu xuất con trước phiếu cân đối cha.
        await db.Issues.Where(x => x.ParentIssueId != null).ExecuteDeleteAsync();
        await db.Issues.Where(x => x.ParentIssueId == null).ExecuteDeleteAsync();
        await db.StockPeriods.ExecuteDeleteAsync();
        await db.Items.ExecuteDeleteAsync();
        await db.SubordinateUnits.ExecuteDeleteAsync();
        await db.AppMeta.ExecuteDeleteAsync();

        await tx.CommitAsync();
    }

    public async Task BackupDatabaseAsync(string backupFilePath)
    {
        if (string.IsNullOrWhiteSpace(backupFilePath))
        {
            throw new InvalidOperationException("Đường dẫn backup không hợp lệ.");
        }

        var dbPath = await GetDatabasePathAsync();
        if (!File.Exists(dbPath))
        {
            throw new InvalidOperationException("Không tìm thấy dữ liệu để backup.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath)!);
        await using var source = new SqliteConnection($"Data Source={dbPath}");
        await using var destination = new SqliteConnection($"Data Source={backupFilePath}");
        await source.OpenAsync();
        await destination.OpenAsync();
        source.BackupDatabase(destination);
    }

    public async Task RestoreDatabaseAsync(string sourceBackupFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceBackupFilePath) || !File.Exists(sourceBackupFilePath))
        {
            throw new InvalidOperationException("Không tìm thấy file backup để restore.");
        }

        var dbPath = await GetDatabasePathAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        SqliteConnection.ClearAllPools();
        DeleteSqliteSidecarFiles(dbPath);
        File.Copy(sourceBackupFilePath, dbPath, overwrite: true);
    }

    private static void DeleteSqliteSidecarFiles(string databasePath)
    {
        foreach (var sidecarPath in new[] { $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(sidecarPath))
            {
                File.Delete(sidecarPath);
            }
        }
    }

    public async Task<Item> AddItemAsync(Item item)
    {
        if (string.IsNullOrWhiteSpace(item.ItemName) || item.UnitPrice < 0)
        {
            throw new InvalidOperationException("Tên mặt hàng bắt buộc, đơn giá không được âm.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        item.ItemName = item.ItemName.Trim();
        item.Category = ItemVariantKey.NormalizeCategory(item.Category);
        item.Unit = item.Unit.Trim();
        item.UsageType = ItemVariantKey.NormalizeUsageType(item.UsageType);
        var existsPair = await FindMatchingVariantItemAsync(
            db, item.ItemName, item.UnitPrice, item.Category, item.UsageType);
        if (existsPair is not null)
        {
            throw new InvalidOperationException(
                "Mặt hàng trùng tên, đơn giá, danh mục và loại hàng đã tồn tại.");
        }

        if (string.IsNullOrWhiteSpace(item.ItemCode))
        {
            item.ItemCode = await GenerateUniqueItemCodeAsync(
                db, item.ItemName, item.UnitPrice, item.Category, item.UsageType);
        }

        var existsCode = await db.Items.AnyAsync(x => x.ItemCode == item.ItemCode);
        if (existsCode)
        {
            throw new InvalidOperationException($"Mã hàng '{item.ItemCode}' đã tồn tại.");
        }

        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    public async Task UpdateItemAsync(Item item)
    {
        if (string.IsNullOrWhiteSpace(item.ItemName) || item.UnitPrice < 0)
        {
            throw new InvalidOperationException("Tên mặt hàng bắt buộc, đơn giá không được âm.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var dbItem = await db.Items.SingleOrDefaultAsync(x => x.Id == item.Id)
            ?? throw new InvalidOperationException("Không tìm thấy mặt hàng.");

        var duplicateCode = await db.Items.AnyAsync(x => x.Id != item.Id && x.ItemCode == item.ItemCode);
        if (duplicateCode)
        {
            throw new InvalidOperationException("Mã hàng đã tồn tại.");
        }

        var trimmedName = item.ItemName.Trim();
        var trimmedCategory = ItemVariantKey.NormalizeCategory(item.Category);
        var trimmedUsage = ItemVariantKey.NormalizeUsageType(item.UsageType);
        var duplicatePair = await FindMatchingVariantItemAsync(
            db, trimmedName, item.UnitPrice, trimmedCategory, trimmedUsage);
        if (duplicatePair is not null && duplicatePair.Id != item.Id)
        {
            throw new InvalidOperationException(
                "Đã có mặt hàng cùng tên, đơn giá, danh mục và loại hàng.");
        }

        dbItem.ItemCode = string.IsNullOrWhiteSpace(item.ItemCode)
            ? await GenerateUniqueItemCodeAsync(
                db, trimmedName, item.UnitPrice, trimmedCategory, trimmedUsage)
            : item.ItemCode.Trim();
        dbItem.ItemName = trimmedName;
        dbItem.Category = trimmedCategory;
        dbItem.Unit = item.Unit.Trim();
        dbItem.UsageType = trimmedUsage;
        dbItem.UnitPrice = item.UnitPrice;
        dbItem.Note = item.Note;
        await db.SaveChangesAsync();
    }

    public async Task UpsertOpeningBalancesAsync(int year, IReadOnlyCollection<OpeningBalanceInput> lines)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Không có dòng dữ liệu tồn đầu năm.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var duplicatePairs = lines
            .GroupBy(x => ItemVariantKey.Build(x.ItemName, x.UnitPrice, x.Category, x.UsageType))
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var sample = g.First();
                return $"{sample.ItemName.Trim()} / {sample.UnitPrice:N0} / {sample.Category.Trim()} / {ItemVariantKey.NormalizeUsageType(sample.UsageType)}";
            })
            .ToList();

        if (duplicatePairs.Count > 0)
        {
            throw new InvalidOperationException(
                $"File tồn đầu bị trùng tên + đơn giá + danh mục + loại hàng: {string.Join("; ", duplicatePairs)}.");
        }

        foreach (var line in lines)
        {
            if (line.OpeningQuantity < 0)
            {
                throw new InvalidOperationException($"Số lượng tồn đầu không hợp lệ: {line.ItemName}.");
            }

            if (string.IsNullOrWhiteSpace(line.ItemName)
                || string.IsNullOrWhiteSpace(line.Unit)
                || line.UnitPrice < 0)
            {
                throw new InvalidOperationException("Dữ liệu tồn đầu năm bắt buộc có TênMặtHàng, ĐVT và ĐơnGiá >= 0.");
            }

            var trimmedName = line.ItemName.Trim();
            var category = ItemVariantKey.NormalizeCategory(line.Category);
            var usageType = ItemVariantKey.NormalizeUsageType(line.UsageType);
            var item = await FindMatchingVariantItemAsync(
                db, trimmedName, line.UnitPrice, category, usageType);
            if (item is null)
            {
                var autoCode = await GenerateUniqueItemCodeAsync(
                    db, trimmedName, line.UnitPrice, category, usageType);
                var byCode = await db.Items.SingleOrDefaultAsync(x => x.ItemCode == autoCode);
                if (byCode is not null
                    && ItemVariantKey.Matches(byCode, trimmedName, line.UnitPrice, category, usageType))
                {
                    item = byCode;
                }
                else if (byCode is not null)
                {
                    throw new InvalidOperationException(
                        $"Không thể tạo mặt hàng mới vì mã '{autoCode}' đã tồn tại với biến thể khác.");
                }
                else
                {
                    item = new Item
                    {
                        ItemCode = autoCode,
                        ItemName = trimmedName,
                        Category = category,
                        Unit = line.Unit.Trim(),
                        UsageType = usageType,
                        UnitPrice = line.UnitPrice
                    };
                    db.Items.Add(item);
                    await db.SaveChangesAsync();
                }
            }
            else
            {
                // Không ghi đè danh mục/loại (đã khớp khóa). Chỉ cập nhật ĐVT nếu có.
                item.Unit = line.Unit.Trim();
            }

            var openingNote = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim();

            var period = await db.StockPeriods.SingleOrDefaultAsync(x => x.Year == year && x.ItemId == item.Id);
            if (period is null)
            {
                period = new StockPeriod
                {
                    Year = year,
                    ItemId = item.Id,
                    OpeningQuantity = line.OpeningQuantity,
                    ReceivedQuantity = 0,
                    IssuedQuantity = 0,
                    ClosingQuantity = line.OpeningQuantity,
                    UnitPrice = line.UnitPrice,
                    IsManualAdjustment = true,
                    Note = openingNote
                };
                db.StockPeriods.Add(period);
            }
            else
            {
                period.OpeningQuantity = line.OpeningQuantity;
                period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
                period.UnitPrice = line.UnitPrice;
                period.IsManualAdjustment = true;
                period.Note = openingNote;
            }

            TryApplyCatalogNoteFromSource(item, openingNote, DateTime.Now, syncEmpty: false);
        }

        await db.SaveChangesAsync();
    }

    public async Task<YearStockActivitySummary> GetYearStockActivityAsync(int year)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var periods = await db.StockPeriods.Where(x => x.Year == year).ToListAsync();
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = new DateTime(year, 12, 31);

        var receiptCount = await db.Receipts.CountAsync(x => x.ReceiptDate >= yearStart && x.ReceiptDate <= yearEnd);
        var issueCount = await db.Issues.CountAsync(x =>
            x.ParentIssueId == null
            && x.IssueDate >= yearStart
            && x.IssueDate <= yearEnd);

        return new YearStockActivitySummary(
            Year: year,
            PeriodCount: periods.Count,
            ItemsWithMovementCount: periods.Count(p => p.ReceivedQuantity != 0 || p.IssuedQuantity != 0),
            TotalReceived: periods.Sum(p => p.ReceivedQuantity),
            TotalIssued: periods.Sum(p => p.IssuedQuantity),
            ReceiptVoucherCount: receiptCount,
            BalanceIssueVoucherCount: issueCount);
    }

    public async Task CarryForwardPeriodAsync(int fromYear, int toYear, bool overwriteExisting)
    {
        if (toYear != fromYear + 1)
        {
            throw new InvalidOperationException("Chỉ hỗ trợ chuyển kỳ sang năm liền sau (ví dụ 2025 → 2026).");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var fromPeriods = await db.StockPeriods.Where(x => x.Year == fromYear).ToListAsync();
        if (fromPeriods.Count == 0)
        {
            throw new InvalidOperationException($"Chưa có dữ liệu tồn năm {fromYear}.");
        }

        var targetActivity = await GetYearStockActivityAsync(toYear);
        if (targetActivity.HasStockOrVoucherActivity)
        {
            throw new InvalidOperationException(
                $"Không chuyển kỳ sang năm {toYear} vì năm này đã có phát sinh.\n" +
                $"- Mặt hàng đã nhập/xuất: {targetActivity.ItemsWithMovementCount}\n" +
                $"- Phiếu nhập: {targetActivity.ReceiptVoucherCount}\n" +
                $"- Phiếu cân đối: {targetActivity.BalanceIssueVoucherCount}\n\n" +
                "Hãy chọn năm mới trống, hoặc chỉ chỉnh tồn đầu năm thủ công (không ghi đè nhập/xuất).");
        }

        var existingTargets = await db.StockPeriods.Where(x => x.Year == toYear).ToListAsync();
        if (existingTargets.Count > 0 && !overwriteExisting)
        {
            throw new InvalidOperationException(
                $"Năm {toYear} đã có {existingTargets.Count} dòng tồn đầu. " +
                "Bật ghi đè nếu muốn cập nhật lại từ tồn cuối năm trước.");
        }

        var targetsByItem = existingTargets.ToDictionary(x => x.ItemId);
        foreach (var from in fromPeriods)
        {
            if (!targetsByItem.TryGetValue(from.ItemId, out var target))
            {
                var created = new StockPeriod
                {
                    Year = toYear,
                    ItemId = from.ItemId,
                    OpeningQuantity = from.ClosingQuantity,
                    ReceivedQuantity = 0,
                    IssuedQuantity = 0,
                    ClosingQuantity = from.ClosingQuantity,
                    UnitPrice = from.UnitPrice,
                    IsManualAdjustment = false
                };
                db.StockPeriods.Add(created);
                targetsByItem[from.ItemId] = created;
                continue;
            }

            target.OpeningQuantity = from.ClosingQuantity;
            target.ReceivedQuantity = 0;
            target.IssuedQuantity = 0;
            target.ClosingQuantity = from.ClosingQuantity;
            target.UnitPrice = from.UnitPrice;
            target.IsManualAdjustment = false;
        }

        await db.SaveChangesAsync();
    }

    public async Task<Receipt> CreateReceiptAsync(ReceiptInput input)
    {
        ValidateReceiptInput(input);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        if (await db.Receipts.AnyAsync(x => x.ReceiptNumber == input.ReceiptNumber))
        {
            throw new InvalidOperationException("Số phiếu nhập đã tồn tại.");
        }

        var lines = await ResolveLinesAsync(db, input.Lines, allowCreateMissingItems: true);
        var receipt = new Receipt
        {
            ReceiptNumber = input.ReceiptNumber.Trim(),
            ReceiptDate = input.ReceiptDate.Date,
            DeliveredBy = input.DeliveredBy.Trim(),
            DeliveredUnit = input.DeliveredUnit.Trim(),
            ReceivedBy = input.ReceivedBy.Trim(),
            Reason = input.Reason.Trim()
        };

        foreach (var line in lines)
        {
            receipt.Lines.Add(new ReceiptLine
            {
                ItemId = line.Item.Id,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = line.Quantity * line.UnitPrice,
                Note = line.Note
            });
        }

        await ApplyCatalogNotesFromReceiptLinesAsync(db, lines, input.ReceiptDate.Date);

        receipt.TotalAmount = receipt.Lines.Sum(x => x.Amount);
        receipt.TotalAmountText = VietnameseMoneyTextConverter.Convert(receipt.TotalAmount);
        db.Receipts.Add(receipt);
        var impactedPeriods = new HashSet<(int ItemId, int Year)>();

        foreach (var line in lines)
        {
            var period = await EnsurePeriodAsync(db, line.Item.Id, input.ReceiptDate.Year, line.UnitPrice);
            period.ReceivedQuantity += line.Quantity;
            period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
            period.UnitPrice = line.UnitPrice;
            impactedPeriods.Add((line.Item.Id, input.ReceiptDate.Year));

            db.StockLedgerEntries.Add(new StockLedgerEntry
            {
                ItemId = line.Item.Id,
                TransactionDate = input.ReceiptDate,
                TransactionType = "Receipt",
                DocumentNumber = input.ReceiptNumber,
                QuantityIn = line.Quantity,
                QuantityOut = 0,
                BalanceAfter = 0,
                UnitPrice = line.UnitPrice,
                Amount = line.Quantity * line.UnitPrice
            });
        }

        await db.SaveChangesAsync();
        await RebuildLedgerBalancesAsync(db, impactedPeriods);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return receipt;
    }

    public async Task<Issue> CreateIssueAsync(IssueInput input)
    {
        ValidateIssueInput(input);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        if (await db.Issues.AnyAsync(x => x.IssueNumber == input.IssueNumber))
        {
            throw new InvalidOperationException("Số phiếu cân đối đã tồn tại.");
        }

        var lines = await ResolveLinesAsync(db, input.Lines, allowCreateMissingItems: false, allowZeroQuantity: true);
        await EnsureSufficientStockAsync(db, lines, input.IssueDate.Date);
        var issue = new Issue
        {
            IssueNumber = input.IssueNumber.Trim(),
            IssueDate = input.IssueDate.Date,
            ReceivedBy = string.Empty,
            ReceivedUnit = string.Empty,
            DeliveredBy = string.Empty,
            Reason = input.Reason?.Trim() ?? string.Empty
        };

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var issueLine = new IssueLine
            {
                ItemId = line.Item.Id,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = line.Quantity * line.UnitPrice,
                Note = line.Note
            };

            AddAllocationsToIssueLine(issueLine, line.Allocations);

            issue.Lines.Add(issueLine);
        }

        issue.TotalAmount = issue.Lines.Sum(x => x.Amount);
        issue.TotalAmountText = VietnameseMoneyTextConverter.Convert(issue.TotalAmount);
        db.Issues.Add(issue);
        var impactedPeriods = new HashSet<(int ItemId, int Year)>();

        // Phiếu cân đối trừ tồn ngay khi lưu — không phụ thuộc sinh phiếu xuất theo đơn vị.
        // Dòng SL = 0 vẫn lưu trên phiếu nhưng không ghi sổ / không trừ tồn.
        foreach (var line in lines.Where(x => x.Quantity > 0))
        {
            var period = await EnsurePeriodAsync(db, line.Item.Id, input.IssueDate.Year, line.UnitPrice);
            period.IssuedQuantity += line.Quantity;
            period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
            period.UnitPrice = line.UnitPrice;
            impactedPeriods.Add((line.Item.Id, input.IssueDate.Year));

            db.StockLedgerEntries.Add(new StockLedgerEntry
            {
                ItemId = line.Item.Id,
                TransactionDate = input.IssueDate,
                TransactionType = "Issue",
                DocumentNumber = input.IssueNumber,
                QuantityIn = 0,
                QuantityOut = line.Quantity,
                BalanceAfter = 0,
                UnitPrice = line.UnitPrice,
                Amount = line.Quantity * line.UnitPrice
            });
        }

        await db.SaveChangesAsync();
        await RebuildLedgerBalancesAsync(db, impactedPeriods);
        await db.SaveChangesAsync();

        await SyncUnitIssuesInDbAsync(db, issue);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return issue;
    }

    private static async Task EnsureSufficientStockAsync(
        InventoryDbContext db,
        IReadOnlyCollection<ResolvedLine> lines,
        DateTime issueDate,
        int? excludeIssueId = null)
    {
        var itemIds = lines.Select(x => x.Item.Id).Distinct().ToList();
        var yearStart = new DateTime(issueDate.Year, 1, 1);
        var asOf = issueDate.Date;
        var openings = await db.StockPeriods
            .Where(x => x.Year == issueDate.Year && itemIds.Contains(x.ItemId))
            .Select(x => new { x.ItemId, x.OpeningQuantity })
            .ToDictionaryAsync(x => x.ItemId, x => x.OpeningQuantity);
        var receiptRows = await (
            from line in db.ReceiptLines
            join receipt in db.Receipts on line.ReceiptId equals receipt.Id
            where itemIds.Contains(line.ItemId)
                  && receipt.ReceiptDate >= yearStart
                  && receipt.ReceiptDate <= asOf
            select new { line.ItemId, line.Quantity }
        ).ToListAsync();
        var issueRows = await (
            from line in db.IssueLines
            join issue in db.Issues on line.IssueId equals issue.Id
            where itemIds.Contains(line.ItemId)
                  && issue.ParentIssueId == null
                  && issue.IssueDate >= yearStart
                  && issue.IssueDate <= asOf
                  && (!excludeIssueId.HasValue || issue.Id != excludeIssueId.Value)
            select new { line.ItemId, line.Quantity }
        ).ToListAsync();
        var received = receiptRows
            .GroupBy(x => x.ItemId)
            .ToDictionary(x => x.Key, x => x.Sum(row => row.Quantity));
        var issued = issueRows
            .GroupBy(x => x.ItemId)
            .ToDictionary(x => x.Key, x => x.Sum(row => row.Quantity));

        foreach (var line in lines)
        {
            var available = openings.GetValueOrDefault(line.Item.Id)
                + received.GetValueOrDefault(line.Item.Id)
                - issued.GetValueOrDefault(line.Item.Id);
            if (line.Quantity > available)
            {
                throw new InvalidOperationException(
                    $"Mặt hàng {line.Item.ItemCode} không đủ tồn tại ngày {asOf:dd/MM/yyyy}. " +
                    $"Tồn: {available:N0}, yêu cầu xuất: {line.Quantity:N0}.");
            }
        }
    }

    public async Task<List<DashboardRow>> GetDashboardRowsAsync(int year)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        // AsNoTracking + project nhẹ: không gắn change-tracker khi chỉ hiển thị dashboard.
        var items = await db.Items.AsNoTracking()
            .OrderBy(x => x.Category)
            .ThenBy(x => x.ItemName)
            .ThenBy(x => x.ItemCode)
            .Select(x => new
            {
                x.Id,
                x.ItemCode,
                x.Category,
                x.ItemName,
                x.Unit,
                x.UnitPrice,
                x.UsageType,
                x.Note
            })
            .ToListAsync();

        var periods = await db.StockPeriods.AsNoTracking()
            .Where(x => x.Year == year)
            .Select(x => new
            {
                x.ItemId,
                x.OpeningQuantity,
                x.ReceivedQuantity,
                x.IssuedQuantity,
                x.ClosingQuantity
            })
            .ToDictionaryAsync(x => x.ItemId);

        var rows = new List<DashboardRow>(items.Count);
        foreach (var item in items)
        {
            periods.TryGetValue(item.Id, out var period);
            var opening = period?.OpeningQuantity ?? 0m;
            var received = period?.ReceivedQuantity ?? 0m;
            var issued = period?.IssuedQuantity ?? 0m;
            var closing = period?.ClosingQuantity ?? (opening + received - issued);
            rows.Add(new DashboardRow
            {
                ItemId = item.Id,
                ItemCode = item.ItemCode,
                Category = item.Category,
                ItemName = item.ItemName,
                Unit = item.Unit,
                UnitPrice = item.UnitPrice,
                UsageType = item.UsageType,
                OpeningQuantity = opening,
                ReceivedQuantity = received,
                IssuedQuantity = issued,
                ClosingQuantity = closing,
                Amount = closing * item.UnitPrice,
                Note = item.Note
            });
        }

        return rows;
    }

    public async Task UpdateItemsMetadataAsync(IReadOnlyList<ItemMetadataPatch> patches)
    {
        if (patches.Count == 0)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var ids = patches.Select(p => p.ItemId).Distinct().ToList();
        var items = await db.Items.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var now = DateTime.Now;

        foreach (var p in patches)
        {
            if (!items.TryGetValue(p.ItemId, out var item))
            {
                throw new InvalidOperationException($"Không tìm thấy mặt hàng Id={p.ItemId}.");
            }

            var category = ItemVariantKey.NormalizeCategory(p.Category);
            var usageType = ItemVariantKey.NormalizeUsageType(p.UsageType);
            var conflict = await db.Items.AnyAsync(x =>
                x.Id != item.Id
                && x.ItemName == item.ItemName
                && x.UnitPrice == item.UnitPrice
                && x.Category == category
                && x.UsageType == usageType);
            if (conflict)
            {
                throw new InvalidOperationException(
                    $"Mã '{item.ItemCode}': đã có mặt hàng khác cùng tên, đơn giá, danh mục và loại hàng.");
            }

            item.Category = category;
            item.UsageType = usageType;

            var newNote = string.IsNullOrWhiteSpace(p.Note) ? null : p.Note.Trim();
            if (!string.Equals(item.Note, newNote, StringComparison.Ordinal))
            {
                item.Note = newNote;
                // Gắn ghi chú trên Dashboard — chỉ bị phiếu nhập/tồn đầu sau thời điểm này ghi đè.
                item.NoteManualAt = now;
            }
        }

        await db.SaveChangesAsync();
    }

    private static string NormalizeUsageTypeForStorage(string usageType) =>
        ItemVariantKey.NormalizeUsageType(usageType);

    public async Task<List<OpeningBalanceInput>> GetOpeningBalancesForYearAsync(int year)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var periods = await db.StockPeriods.AsNoTracking()
            .Where(x => x.Year == year && (x.OpeningQuantity != 0 || x.IsManualAdjustment))
            .ToDictionaryAsync(x => x.ItemId);

        if (periods.Count == 0)
        {
            return [];
        }

        var itemIds = periods.Keys.ToList();
        var items = await db.Items.AsNoTracking()
            .Where(x => itemIds.Contains(x.Id))
            .OrderBy(x => x.Category)
            .ThenBy(x => x.ItemName)
            .ThenBy(x => x.ItemCode)
            .Select(x => new { x.Category, x.ItemName, x.Unit, x.UnitPrice, x.UsageType, x.Id })
            .ToListAsync();

        return items.Select(item =>
        {
            periods.TryGetValue(item.Id, out var period);
            return new OpeningBalanceInput
            {
                Category = item.Category,
                ItemName = item.ItemName,
                Unit = item.Unit,
                UnitPrice = item.UnitPrice,
                UsageType = item.UsageType,
                OpeningQuantity = period?.OpeningQuantity ?? 0m,
                Note = period?.Note
            };
        }).ToList();
    }

    public async Task<List<Receipt>> GetReceiptsAsync(int? year = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var query = db.Receipts.AsNoTracking().AsQueryable();
        if (year is int y)
        {
            var yearStart = new DateTime(y, 1, 1);
            var yearEnd = new DateTime(y, 12, 31);
            query = query.Where(x => x.ReceiptDate >= yearStart && x.ReceiptDate <= yearEnd);
        }

        var receipts = await query
            .OrderByDescending(x => x.ReceiptDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        await PopulateReceiptTypeAmountsAsync(db, receipts);
        return receipts;
    }

    public async Task<List<Issue>> GetIssuesAsync(int? year = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var query = db.Issues.AsNoTracking().Where(x => x.ParentIssueId == null);
        if (year is int y)
        {
            var yearStart = new DateTime(y, 1, 1);
            var yearEnd = new DateTime(y, 12, 31);
            query = query.Where(x => x.IssueDate >= yearStart && x.IssueDate <= yearEnd);
        }

        var issues = await query
            .OrderByDescending(x => x.IssueDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        await PopulateIssueTypeAmountsAsync(db, issues);

        var parentIds = issues.Select(x => x.Id).ToList();
        if (parentIds.Count > 0)
        {
            var childCounts = await db.Issues.AsNoTracking()
                .Where(x => x.ParentIssueId != null && parentIds.Contains(x.ParentIssueId.Value))
                .GroupBy(x => x.ParentIssueId!.Value)
                .Select(g => new { ParentId = g.Key, Count = g.Count() })
                .ToListAsync();
            var map = childCounts.ToDictionary(x => x.ParentId, x => x.Count);
            foreach (var issue in issues)
            {
                issue.GeneratedUnitIssueCount = map.GetValueOrDefault(issue.Id);
            }
        }

        return issues;
    }

    public async Task<List<Issue>> GetGeneratedUnitIssuesAsync(int balanceIssueId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var children = await db.Issues
            .Include(x => x.Lines)
            .Where(x => x.ParentIssueId == balanceIssueId)
            .OrderBy(x => x.IssueNumber)
            .ToListAsync();
        var unitIds = children
            .Where(x => x.SubordinateUnitId.HasValue)
            .Select(x => x.SubordinateUnitId!.Value)
            .Distinct()
            .ToList();
        var unitCodes = await db.SubordinateUnits.AsNoTracking()
            .Where(x => unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UnitCode);
        foreach (var child in children)
        {
            if (child.SubordinateUnitId is int unitId)
            {
                child.ReceivedUnitCode = unitCodes.GetValueOrDefault(unitId) ?? string.Empty;
            }
        }

        return children;
    }

    public async Task<List<UnitIssueReportRow>> GetUnitIssueReportAsync(
        DateTime fromDate,
        DateTime toDate,
        int? subordinateUnitId = null,
        string? keyword = null)
    {
        var fromInclusive = fromDate.Date;
        var toExclusive = toDate.Date.AddDays(1);
        if (toExclusive <= fromInclusive)
        {
            throw new InvalidOperationException("Khoảng thời gian báo cáo không hợp lệ.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var query =
            from issue in db.Issues.AsNoTracking()
            join parent in db.Issues.AsNoTracking()
                on issue.ParentIssueId equals (int?)parent.Id
            join unit in db.SubordinateUnits.AsNoTracking()
                on issue.SubordinateUnitId equals (int?)unit.Id into joinedUnits
            from unit in joinedUnits.DefaultIfEmpty()
            where issue.ParentIssueId != null
                  && issue.IssueDate >= fromInclusive
                  && issue.IssueDate < toExclusive
                  && (!subordinateUnitId.HasValue || issue.SubordinateUnitId == subordinateUnitId)
            select new
            {
                Issue = issue,
                ParentIssueNumber = parent.IssueNumber,
                UnitCode = unit == null ? string.Empty : unit.UnitCode,
                UnitName = unit == null ? issue.ReceivedUnit : unit.Name,
                UnitSortOrder = unit == null ? int.MaxValue : unit.SortOrder,
                LineCount = issue.Lines.Count
            };

        var headers = await query
            .OrderBy(x => x.UnitSortOrder)
            .ThenBy(x => x.UnitName)
            .ThenBy(x => x.Issue.IssueDate)
            .ThenBy(x => x.Issue.IssueNumber)
            .ToListAsync();

        var normalizedKeyword = keyword?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            headers = headers.Where(x =>
                    ContainsIgnoreCase(x.Issue.IssueNumber, normalizedKeyword)
                    || ContainsIgnoreCase(x.ParentIssueNumber, normalizedKeyword)
                    || ContainsIgnoreCase(x.UnitCode, normalizedKeyword)
                    || ContainsIgnoreCase(x.UnitName, normalizedKeyword)
                    || ContainsIgnoreCase(x.Issue.ReceivedBy, normalizedKeyword)
                    || ContainsIgnoreCase(x.Issue.Reason, normalizedKeyword))
                .ToList();
        }

        var issueIds = headers.Select(x => x.Issue.Id).ToList();
        var quantities = issueIds.Count == 0
            ? new Dictionary<int, decimal>()
            : (await db.IssueLines.AsNoTracking()
                    .Where(x => issueIds.Contains(x.IssueId))
                    .Select(x => new { x.IssueId, x.Quantity })
                    .ToListAsync())
                .GroupBy(x => x.IssueId)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));

        return headers.Select(x => new UnitIssueReportRow(
                x.Issue.Id,
                x.Issue.ParentIssueId!.Value,
                x.ParentIssueNumber,
                x.Issue.IssueDate,
                x.Issue.IssueNumber,
                x.Issue.SubordinateUnitId,
                x.UnitCode,
                x.UnitName,
                x.Issue.ReceivedBy,
                x.Issue.DeliveredBy,
                x.Issue.Reason,
                x.LineCount,
                quantities.GetValueOrDefault(x.Issue.Id),
                x.Issue.TotalAmount))
            .ToList();
    }

    public async Task<List<Issue>> GetUnitIssuesByIdsAsync(IReadOnlyCollection<int> issueIds)
    {
        if (issueIds.Count == 0)
        {
            return [];
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var ids = issueIds.Distinct().ToList();
        var issues = await db.Issues.AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => ids.Contains(x.Id) && x.ParentIssueId != null)
            .OrderBy(x => x.IssueDate)
            .ThenBy(x => x.IssueNumber)
            .ToListAsync();
        var unitIds = issues.Where(x => x.SubordinateUnitId.HasValue)
            .Select(x => x.SubordinateUnitId!.Value)
            .Distinct()
            .ToList();
        var codes = await db.SubordinateUnits.AsNoTracking()
            .Where(x => unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UnitCode);
        foreach (var issue in issues)
        {
            if (issue.SubordinateUnitId is int unitId)
            {
                issue.ReceivedUnitCode = codes.GetValueOrDefault(unitId) ?? string.Empty;
            }
        }

        return issues;
    }

    public async Task<List<UnitIssueReportLine>> GetUnitIssueReportLinesAsync(
        IReadOnlyCollection<int> issueIds)
    {
        if (issueIds.Count == 0)
        {
            return [];
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var ids = issueIds.Distinct().ToList();
        return await (
                from line in db.IssueLines.AsNoTracking()
                join issue in db.Issues.AsNoTracking() on line.IssueId equals issue.Id
                join item in db.Items.AsNoTracking() on line.ItemId equals item.Id
                join unit in db.SubordinateUnits.AsNoTracking()
                    on issue.SubordinateUnitId equals (int?)unit.Id into joinedUnits
                from subordinateUnit in joinedUnits.DefaultIfEmpty()
                where ids.Contains(issue.Id) && issue.ParentIssueId != null
                orderby issue.IssueDate, issue.IssueNumber, line.Id
                select new UnitIssueReportLine(
                    issue.Id,
                    issue.IssueNumber,
                    issue.IssueDate,
                    subordinateUnit == null ? string.Empty : subordinateUnit.UnitCode,
                    subordinateUnit == null ? issue.ReceivedUnit : subordinateUnit.Name,
                    item.Id,
                    item.ItemCode,
                    item.Category,
                    item.ItemName,
                    item.Unit,
                    line.Quantity,
                    line.UnitPrice,
                    line.Amount,
                    line.Note,
                    item.UsageType))
            .ToListAsync();
    }

    private static bool ContainsIgnoreCase(string? source, string value) =>
        source?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Đồng bộ phiếu xuất theo đơn vị từ phân bổ của phiếu cân đối (chỉ để in, không trừ tồn).
    /// </summary>
    public async Task<List<Issue>> SyncUnitIssuesFromBalanceAsync(int balanceIssueId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var balance = await db.Issues
            .Include(x => x.Lines).ThenInclude(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == balanceIssueId && x.ParentIssueId == null)
            ?? throw new InvalidOperationException("Không tìm thấy phiếu cân đối.");

        var created = await SyncUnitIssuesInDbAsync(db, balance);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return created;
    }

    private static async Task<List<Issue>> SyncUnitIssuesInDbAsync(InventoryDbContext db, Issue balance)
    {
        var units = await db.SubordinateUnits.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync();
        var oldChildren = await db.Issues
            .Include(x => x.Lines)
            .Where(x => x.ParentIssueId == balance.Id)
            .ToListAsync();

        var preservedHeaders = oldChildren
            .Where(x => x.SubordinateUnitId.HasValue)
            .ToDictionary(
                x => x.SubordinateUnitId!.Value,
                x => (
                    x.IssueNumber,
                    x.IssueDate,
                    x.ReceivedBy,
                    x.DeliveredBy,
                    x.Reason,
                    x.ReceivedUnit));

        if (oldChildren.Count > 0)
        {
            // Phiếu con chỉ để in — nếu còn sót dòng sổ cái xuất theo số phiếu con thì xóa.
            var oldChildNumbers = oldChildren.Select(x => x.IssueNumber).Distinct().ToList();
            var orphanChildLedgers = await db.StockLedgerEntries
                .Where(x => x.TransactionType == "Issue" && oldChildNumbers.Contains(x.DocumentNumber))
                .ToListAsync();
            if (orphanChildLedgers.Count > 0)
            {
                db.StockLedgerEntries.RemoveRange(orphanChildLedgers);
            }

            var oldLineIds = oldChildren.SelectMany(c => c.Lines.Select(l => l.Id)).ToList();
            if (oldLineIds.Count > 0)
            {
                var oldAlloc = await db.IssueLineAllocations.Where(x => oldLineIds.Contains(x.IssueLineId)).ToListAsync();
                db.IssueLineAllocations.RemoveRange(oldAlloc);
            }

            db.Issues.RemoveRange(oldChildren);
            await db.SaveChangesAsync();
        }

        var existingNumbers = await db.Issues.Select(x => x.IssueNumber).ToListAsync();
        var used = new HashSet<string>(existingNumbers, StringComparer.OrdinalIgnoreCase);
        var created = new List<Issue>();
        var index = 1;

        // Chỉ tạo phiếu xuất con khi đơn vị thực sự nhận hàng (có phân bổ SL > 0).
        foreach (var unit in units)
        {
            var unitLines = new List<IssueLine>();
            foreach (var line in balance.Lines)
            {
                var alloc = line.Allocations.FirstOrDefault(a => a.SubordinateUnitId == unit.Id && a.Quantity > 0);
                if (alloc is null)
                {
                    continue;
                }

                unitLines.Add(new IssueLine
                {
                    ItemId = line.ItemId,
                    Quantity = alloc.Quantity,
                    UnitPrice = line.UnitPrice,
                    Amount = alloc.Quantity * line.UnitPrice,
                    Note = line.Note
                });
            }

            if (unitLines.Count == 0)
            {
                continue;
            }

            var hasPreserved = preservedHeaders.TryGetValue(unit.Id, out var preserved);

            string number;
            if (hasPreserved
                && !string.IsNullOrWhiteSpace(preserved.IssueNumber)
                && !used.Contains(preserved.IssueNumber))
            {
                number = preserved.IssueNumber;
            }
            else
            {
                number = SuggestUniqueNumber(balance.IssueNumber, index, used);
            }

            used.Add(number);
            index++;

            var unitIssue = new Issue
            {
                IssueNumber = number,
                IssueDate = hasPreserved ? preserved.IssueDate.Date : balance.IssueDate.Date,
                ReceivedBy = hasPreserved ? preserved.ReceivedBy : unit.ReceivedBy,
                ReceivedUnit = unit.Name,
                ReceivedUnitCode = unit.UnitCode,
                DeliveredBy = hasPreserved ? preserved.DeliveredBy : unit.DeliveredBy,
                Reason = hasPreserved
                    ? preserved.Reason
                    : balance.Reason,
                ParentIssueId = balance.Id,
                SubordinateUnitId = unit.Id,
                Lines = unitLines,
                TotalAmount = unitLines.Sum(x => x.Amount)
            };
            unitIssue.TotalAmountText = VietnameseMoneyTextConverter.Convert(unitIssue.TotalAmount);
            db.Issues.Add(unitIssue);
            created.Add(unitIssue);
        }

        return created;
    }

    /// <summary>
    /// Cập nhật ngày và thông tin phụ của phiếu xuất theo đơn vị (không ảnh hưởng tồn kho).
    /// </summary>
    public Task UpdateUnitIssueHeadersAsync(int balanceIssueId, IReadOnlyList<UnitIssueHeaderPatch> patches) =>
        UpdateUnitIssueHeadersCoreAsync(patches, balanceIssueId);

    /// <summary>Cập nhật nguyên tử nhiều phiếu xuất con, kể cả khi thuộc các phiếu cân đối khác nhau.</summary>
    public Task UpdateUnitIssueHeadersAsync(IReadOnlyList<UnitIssueHeaderPatch> patches) =>
        UpdateUnitIssueHeadersCoreAsync(patches, expectedBalanceIssueId: null);

    private async Task UpdateUnitIssueHeadersCoreAsync(
        IReadOnlyList<UnitIssueHeaderPatch> patches,
        int? expectedBalanceIssueId)
    {
        if (patches.Count == 0)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        if (expectedBalanceIssueId is int balanceIssueId
            && !await db.Issues.AnyAsync(x => x.Id == balanceIssueId && x.ParentIssueId == null))
        {
            throw new InvalidOperationException("Không tìm thấy phiếu cân đối.");
        }

        var ids = patches.Select(p => p.IssueId).Distinct().ToList();
        var children = await db.Issues
            .Where(x =>
                ids.Contains(x.Id)
                && x.ParentIssueId != null
                && (!expectedBalanceIssueId.HasValue || x.ParentIssueId == expectedBalanceIssueId))
            .ToListAsync();

        if (children.Count != ids.Count)
        {
            throw new InvalidOperationException(
                expectedBalanceIssueId.HasValue
                    ? "Một số phiếu xuất không thuộc phiếu cân đối này."
                    : "Một số phiếu không phải phiếu xuất theo đơn vị.");
        }

        var normalizedPatches = patches.Select(p => new
        {
            Patch = p,
            Number = p.IssueNumber?.Trim() ?? string.Empty
        }).ToList();
        if (normalizedPatches.Any(x => string.IsNullOrWhiteSpace(x.Number)))
        {
            throw new InvalidOperationException("Số phiếu xuất không được để trống.");
        }

        var duplicateNumbers = normalizedPatches
            .Select(x => x.Number)
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateNumbers.Count > 0)
        {
            throw new InvalidOperationException($"Số phiếu xuất bị trùng trong danh sách: {string.Join(", ", duplicateNumbers)}.");
        }

        var existingNumbers = (await db.Issues.AsNoTracking()
                .Where(x => !ids.Contains(x.Id))
                .Select(x => x.IssueNumber)
                .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflictNumber = normalizedPatches.Select(x => x.Number)
            .FirstOrDefault(existingNumbers.Contains);
        if (conflictNumber is not null)
        {
            throw new InvalidOperationException($"Số phiếu xuất đã tồn tại: {conflictNumber}.");
        }

        foreach (var value in normalizedPatches)
        {
            var patch = value.Patch;
            var child = children.Single(x => x.Id == patch.IssueId);
            child.IssueNumber = value.Number;
            child.IssueDate = patch.IssueDate.Date;
            child.ReceivedBy = patch.ReceivedBy?.Trim() ?? string.Empty;
            child.DeliveredBy = patch.DeliveredBy?.Trim() ?? string.Empty;
            child.Reason = patch.Reason?.Trim() ?? string.Empty;
        }

        await db.SaveChangesAsync();
    }

    private static string SuggestUniqueNumber(string parentNumber, int index, HashSet<string> used)
    {
        var baseNumber = parentNumber.Trim();
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var candidate = $"{baseNumber}-{index + attempt:00}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        var fallback = $"{baseNumber}-{index:00}-{Guid.NewGuid():N}";
        return fallback.Length <= 50 ? fallback : fallback[..50];
    }

    /// <summary>
    /// Tồn khả dụng theo đúng ngày lập phiếu.
    /// Khi sửa, chỉ tính các phiếu cân đối đứng trước phiếu đang mở.
    /// </summary>
    /// <param name="excludeBalanceIssueId">Id phiếu cân đối đang sửa (bỏ hiệu lực phiếu này khỏi phép trừ tồn).</param>
    public async Task<decimal> GetAvailableStockAsync(string itemCode, DateTime atDate, int? excludeBalanceIssueId = null)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            return 0;
        }

        var map = await GetAvailableStockBatchAsync([itemCode.Trim()], atDate, excludeBalanceIssueId);
        return map.GetValueOrDefault(itemCode.Trim());
    }

    /// <summary>Lấy tồn khả dụng cho nhiều mã hàng trong một lần truy vấn.</summary>
    public async Task<IReadOnlyDictionary<string, decimal>> GetAvailableStockBatchAsync(
        IReadOnlyList<string> itemCodes,
        DateTime atDate,
        int? excludeBalanceIssueId = null)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var codes = itemCodes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0)
        {
            return result;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var year = atDate.Year;
        var yearStart = new DateTime(year, 1, 1);
        var asOf = atDate.Date;

        var exactItems = await db.Items.AsNoTracking()
            .Where(x => codes.Contains(x.ItemCode))
            .Select(x => new { x.Id, x.ItemCode })
            .ToListAsync();

        // Map theo mã (không phân biệt hoa thường) — kể cả mã không tìm thấy → 0.
        var itemByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in exactItems)
        {
            itemByCode[item.ItemCode] = item.Id;
        }

        var unresolvedCodes = codes
            .Where(code => !itemByCode.ContainsKey(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (unresolvedCodes.Count > 0)
        {
            // SQLite NOCASE/UPPER chỉ xử lý ASCII. Lọc phần chưa khớp ở client để
            // mã Unicode vẫn thực sự không phân biệt hoa thường.
            var candidates = await db.Items.AsNoTracking()
                .Select(x => new { x.Id, x.ItemCode })
                .ToListAsync();
            foreach (var item in candidates.Where(x => unresolvedCodes.Contains(x.ItemCode)))
            {
                itemByCode[item.ItemCode] = item.Id;
            }
        }

        var itemIds = itemByCode.Values.Distinct().ToList();
        var openings = await db.StockPeriods.AsNoTracking()
            .Where(x => x.Year == year && itemIds.Contains(x.ItemId))
            .Select(x => new { x.ItemId, x.OpeningQuantity })
            .ToDictionaryAsync(x => x.ItemId, x => x.OpeningQuantity);

        // SQLite không SUM trực tiếp decimal: tải các dòng cần thiết rồi cộng phía client.
        var receiptRows = await (
            from line in db.ReceiptLines.AsNoTracking()
            join receipt in db.Receipts.AsNoTracking() on line.ReceiptId equals receipt.Id
            where itemIds.Contains(line.ItemId)
                  && receipt.ReceiptDate >= yearStart
                  && receipt.ReceiptDate <= asOf
            select new { line.ItemId, line.Quantity }
        ).ToListAsync();

        var issuedQuery =
            from line in db.IssueLines.AsNoTracking()
            join issue in db.Issues.AsNoTracking() on line.IssueId equals issue.Id
            where itemIds.Contains(line.ItemId)
                  && issue.ParentIssueId == null
                  && issue.IssueDate >= yearStart
                  && issue.IssueDate <= asOf
            select new { line.ItemId, line.Quantity, issue.Id, issue.IssueDate };

        if (excludeBalanceIssueId is int excludeId)
        {
            issuedQuery = issuedQuery.Where(x =>
                x.IssueDate < asOf
                || (x.IssueDate == asOf && x.Id < excludeId));
        }

        var issueRows = await issuedQuery.ToListAsync();
        var receivedByItem = receiptRows
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var issuedByItem = issueRows
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        foreach (var code in codes)
        {
            if (!itemByCode.TryGetValue(code, out var itemId))
            {
                result[code] = 0;
                continue;
            }

            result[code] = openings.GetValueOrDefault(itemId)
                + receivedByItem.GetValueOrDefault(itemId)
                - issuedByItem.GetValueOrDefault(itemId);
        }

        return result;
    }

    public async Task<List<int>> GetAvailableYearsAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var today = DateTime.Today.Year;
        var years = new HashSet<int> { today };

        foreach (var y in await db.StockPeriods.AsNoTracking().Select(x => x.Year).Distinct().ToListAsync())
        {
            years.Add(y);
        }

        // Min/Max ngày — tránh load mọi phiếu; không phụ thuộc dịch .Year trên SQLite.
        if (await db.Receipts.AsNoTracking().AnyAsync())
        {
            var min = await db.Receipts.AsNoTracking().MinAsync(x => x.ReceiptDate);
            var max = await db.Receipts.AsNoTracking().MaxAsync(x => x.ReceiptDate);
            for (var y = min.Year; y <= max.Year; y++)
            {
                years.Add(y);
            }
        }

        if (await db.Issues.AsNoTracking().AnyAsync(x => x.ParentIssueId == null))
        {
            var min = await db.Issues.AsNoTracking()
                .Where(x => x.ParentIssueId == null)
                .MinAsync(x => x.IssueDate);
            var max = await db.Issues.AsNoTracking()
                .Where(x => x.ParentIssueId == null)
                .MaxAsync(x => x.IssueDate);
            for (var y = min.Year; y <= max.Year; y++)
            {
                years.Add(y);
            }
        }

        // Dải liên tục + đệm (giống mũi tên năm cũ) để chọn được năm trống / năm trước chuyển kỳ.
        var minYear = Math.Min(years.Min(), today - 10);
        var maxYear = Math.Max(years.Max(), today) + 1;
        return Enumerable.Range(minYear, maxYear - minYear + 1)
            .Reverse()
            .ToList();
    }

    /// <summary>
    /// Đồng bộ IssuedQuantity / sổ cái xuất chỉ từ phiếu cân đối; xóa xuất gắn phiếu con (nếu còn).
    /// </summary>
    public async Task ReconcileIssuedStockFromBalanceIssuesAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var childNumbers = await db.Issues
            .Where(x => x.ParentIssueId != null)
            .Select(x => x.IssueNumber)
            .ToListAsync();

        if (childNumbers.Count > 0)
        {
            var childLedgers = await db.StockLedgerEntries
                .Where(x => x.TransactionType == "Issue" && childNumbers.Contains(x.DocumentNumber))
                .ToListAsync();
            if (childLedgers.Count > 0)
            {
                db.StockLedgerEntries.RemoveRange(childLedgers);
            }
        }

        var oldIssueLedgers = await db.StockLedgerEntries
            .Where(x => x.TransactionType == "Issue")
            .ToListAsync();
        var impactedPeriods = new HashSet<(int ItemId, int Year)>(
            oldIssueLedgers.Select(x => (x.ItemId, x.TransactionDate.Year)));
        db.StockLedgerEntries.RemoveRange(oldIssueLedgers);

        var periods = await db.StockPeriods.ToListAsync();
        foreach (var period in periods)
        {
            period.IssuedQuantity = 0;
            period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity;
        }

        var balanceIssues = await db.Issues
            .Include(x => x.Lines)
            .Where(x => x.ParentIssueId == null)
            .ToListAsync();

        var periodKeys = balanceIssues
            .SelectMany(issue => issue.Lines.Select(line => (line.ItemId, issue.IssueDate.Year)))
            .Distinct()
            .ToList();
        var periodMap = await EnsurePeriodsAsync(db, periodKeys);

        foreach (var issue in balanceIssues)
        {
            foreach (var line in issue.Lines)
            {
                var key = (line.ItemId, issue.IssueDate.Year);
                var period = periodMap[key];
                period.IssuedQuantity += line.Quantity;
                period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
                if (line.UnitPrice > 0)
                {
                    period.UnitPrice = line.UnitPrice;
                }

                db.StockLedgerEntries.Add(new StockLedgerEntry
                {
                    ItemId = line.ItemId,
                    TransactionDate = issue.IssueDate.Date,
                    TransactionType = "Issue",
                    DocumentNumber = issue.IssueNumber,
                    QuantityIn = 0,
                    QuantityOut = line.Quantity,
                    BalanceAfter = 0,
                    UnitPrice = line.UnitPrice,
                    Amount = line.Quantity * line.UnitPrice
                });

                impactedPeriods.Add(key);
            }
        }

        await RebuildLedgerBalancesAsync(db, impactedPeriods);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<Receipt?> GetReceiptByNumberAsync(string receiptNumber)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.Receipts
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.ReceiptNumber == receiptNumber);
    }

    public async Task<Receipt?> GetReceiptByIdAsync(int receiptId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.Receipts
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == receiptId);
    }

    public async Task<Issue?> GetIssueByNumberAsync(string issueNumber)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.Issues
            .Include(x => x.Lines).ThenInclude(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.IssueNumber == issueNumber);
    }

    public async Task<Issue?> GetIssueByIdAsync(int issueId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.Issues
            .Include(x => x.Lines).ThenInclude(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == issueId);
    }

    /// <summary>
    /// Tính tồn ngay trước và sau phiếu cân đối theo ngày lập phiếu.
    /// Với các phiếu cùng ngày, phiếu có Id nhỏ hơn được xem là lập trước.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, BalanceStockSnapshot>> GetBalanceStockSnapshotsAsync(
        int balanceIssueId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var balance = await db.Issues.AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == balanceIssueId && x.ParentIssueId == null)
            ?? throw new InvalidOperationException("Không tìm thấy phiếu cân đối.");

        var itemIds = balance.Lines.Select(x => x.ItemId).Distinct().ToList();
        if (itemIds.Count == 0)
        {
            return new Dictionary<int, BalanceStockSnapshot>();
        }

        var year = balance.IssueDate.Year;
        var yearStart = new DateTime(year, 1, 1);
        var issueDate = balance.IssueDate.Date;

        var openings = await db.StockPeriods.AsNoTracking()
            .Where(x => x.Year == year && itemIds.Contains(x.ItemId))
            .Select(x => new { x.ItemId, x.OpeningQuantity })
            .ToDictionaryAsync(x => x.ItemId, x => x.OpeningQuantity);

        // SQLite không SUM trực tiếp decimal: chỉ tải ItemId/Quantity rồi cộng phía client.
        var receiptRows = await (
            from line in db.ReceiptLines.AsNoTracking()
            join receipt in db.Receipts.AsNoTracking() on line.ReceiptId equals receipt.Id
            where itemIds.Contains(line.ItemId)
                  && receipt.ReceiptDate >= yearStart
                  && receipt.ReceiptDate <= issueDate
            select new { line.ItemId, line.Quantity }
        ).ToListAsync();

        var priorIssueRows = await (
            from line in db.IssueLines.AsNoTracking()
            join issue in db.Issues.AsNoTracking() on line.IssueId equals issue.Id
            where itemIds.Contains(line.ItemId)
                  && issue.ParentIssueId == null
                  && issue.IssueDate >= yearStart
                  && (issue.IssueDate < issueDate
                      || (issue.IssueDate == issueDate && issue.Id < balance.Id))
            select new { line.ItemId, line.Quantity }
        ).ToListAsync();

        var received = receiptRows
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var issuedBefore = priorIssueRows
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var currentIssue = balance.Lines
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        return itemIds.ToDictionary(
            itemId => itemId,
            itemId =>
            {
                var total = openings.GetValueOrDefault(itemId)
                    + received.GetValueOrDefault(itemId)
                    - issuedBefore.GetValueOrDefault(itemId);
                return new BalanceStockSnapshot(
                    total,
                    total - currentIssue.GetValueOrDefault(itemId));
            });
    }

    public async Task<Receipt> UpdateReceiptAsync(int receiptId, ReceiptInput input)
    {
        ValidateReceiptInput(input);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var receipt = await db.Receipts.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == receiptId)
            ?? throw new InvalidOperationException("Không tìm thấy phiếu nhập cần sửa.");

        if (!receipt.ReceiptNumber.Equals(input.ReceiptNumber.Trim(), StringComparison.OrdinalIgnoreCase)
            && await db.Receipts.AnyAsync(x => x.Id != receiptId && x.ReceiptNumber == input.ReceiptNumber.Trim()))
        {
            throw new InvalidOperationException("Số phiếu nhập đã tồn tại.");
        }

        var impactedPeriods = new HashSet<(int ItemId, int Year)>();
        var previousItemIds = receipt.Lines.Select(x => x.ItemId).Distinct().ToList();
        foreach (var line in receipt.Lines)
        {
            var period = await EnsurePeriodAsync(db, line.ItemId, receipt.ReceiptDate.Year, line.UnitPrice);
            if (period.ReceivedQuantity < line.Quantity)
            {
                throw new InvalidOperationException("Dữ liệu tồn kho không hợp lệ để sửa phiếu nhập.");
            }

            period.ReceivedQuantity -= line.Quantity;
            period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
            impactedPeriods.Add((line.ItemId, receipt.ReceiptDate.Year));
        }

        var oldLedger = await db.StockLedgerEntries
            .Where(x => x.TransactionType == "Receipt" && x.DocumentNumber == receipt.ReceiptNumber)
            .ToListAsync();
        db.StockLedgerEntries.RemoveRange(oldLedger);
        db.ReceiptLines.RemoveRange(receipt.Lines);
        receipt.Lines.Clear();

        var resolved = await ResolveLinesAsync(db, input.Lines, allowCreateMissingItems: true);
        receipt.ReceiptNumber = input.ReceiptNumber.Trim();
        receipt.ReceiptDate = input.ReceiptDate.Date;
        receipt.DeliveredBy = input.DeliveredBy.Trim();
        receipt.DeliveredUnit = input.DeliveredUnit.Trim();
        receipt.ReceivedBy = input.ReceivedBy.Trim();
        receipt.Reason = input.Reason.Trim();

        foreach (var line in resolved)
        {
            receipt.Lines.Add(new ReceiptLine
            {
                ItemId = line.Item.Id,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = line.Quantity * line.UnitPrice,
                Note = line.Note
            });
        }

        await ApplyCatalogNotesFromReceiptLinesAsync(db, resolved, input.ReceiptDate.Date, excludeReceiptId: receiptId);

        receipt.TotalAmount = receipt.Lines.Sum(x => x.Amount);
        receipt.TotalAmountText = VietnameseMoneyTextConverter.Convert(receipt.TotalAmount);

        foreach (var line in resolved)
        {
            var period = await EnsurePeriodAsync(db, line.Item.Id, input.ReceiptDate.Year, line.UnitPrice);
            period.ReceivedQuantity += line.Quantity;
            period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
            period.UnitPrice = line.UnitPrice;
            impactedPeriods.Add((line.Item.Id, input.ReceiptDate.Year));

            db.StockLedgerEntries.Add(new StockLedgerEntry
            {
                ItemId = line.Item.Id,
                TransactionDate = input.ReceiptDate.Date,
                TransactionType = "Receipt",
                DocumentNumber = input.ReceiptNumber.Trim(),
                QuantityIn = line.Quantity,
                QuantityOut = 0,
                BalanceAfter = 0,
                UnitPrice = line.UnitPrice,
                Amount = line.Quantity * line.UnitPrice
            });
        }

        await RebuildLedgerBalancesAsync(db, impactedPeriods);
        await db.SaveChangesAsync();

        var remainingItemIds = resolved.Select(x => x.Item.Id).ToHashSet();
        var droppedItemIds = previousItemIds.Where(id => !remainingItemIds.Contains(id)).ToList();
        await RemoveUnusedItemsAsync(db, droppedItemIds);
        await db.SaveChangesAsync();

        // Mặt hàng bị loại khỏi phiếu nhưng vẫn còn: dựng lại ghi chú từ nguồn còn lại.
        var droppedSurvivors = await db.Items.Where(x => droppedItemIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        if (droppedSurvivors.Count > 0)
        {
            await RefreshCatalogNotesForItemsAsync(db, droppedSurvivors, input.ReceiptDate.Year);
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();
        return receipt;
    }

    public async Task<Issue> UpdateIssueAsync(int issueId, IssueInput input)
    {
        ValidateIssueInput(input);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var issue = await db.Issues.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == issueId)
            ?? throw new InvalidOperationException("Không tìm thấy phiếu cân đối cần sửa.");

        if (issue.ParentIssueId is not null)
        {
            throw new InvalidOperationException("Không thể sửa phiếu xuất theo đơn vị. Hãy sửa phiếu cân đối.");
        }

        if (!issue.IssueNumber.Equals(input.IssueNumber.Trim(), StringComparison.OrdinalIgnoreCase)
            && await db.Issues.AnyAsync(x => x.Id != issueId && x.IssueNumber == input.IssueNumber.Trim()))
        {
            throw new InvalidOperationException("Số phiếu cân đối đã tồn tại.");
        }

        var impactedPeriods = new HashSet<(int ItemId, int Year)>();
        foreach (var line in issue.Lines)
        {
            var period = await EnsurePeriodAsync(db, line.ItemId, issue.IssueDate.Year, line.UnitPrice);
            if (period.IssuedQuantity < line.Quantity)
            {
                throw new InvalidOperationException("Dữ liệu tồn kho không hợp lệ để sửa phiếu cân đối.");
            }

            period.IssuedQuantity -= line.Quantity;
            period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
            impactedPeriods.Add((line.ItemId, issue.IssueDate.Year));
        }

        var oldLineIds = issue.Lines.Select(x => x.Id).ToList();
        var oldLedger = await db.StockLedgerEntries
            .Where(x => x.TransactionType == "Issue" && x.DocumentNumber == issue.IssueNumber)
            .ToListAsync();
        db.StockLedgerEntries.RemoveRange(oldLedger);
        if (oldLineIds.Count > 0)
        {
            var oldAllocations = await db.IssueLineAllocations
                .Where(x => oldLineIds.Contains(x.IssueLineId))
                .ToListAsync();
            db.IssueLineAllocations.RemoveRange(oldAllocations);
        }

        db.IssueLines.RemoveRange(issue.Lines);
        issue.Lines.Clear();

        var resolved = await ResolveLinesAsync(db, input.Lines, allowCreateMissingItems: false, allowZeroQuantity: true);
        await EnsureSufficientStockAsync(db, resolved, input.IssueDate.Date, issue.Id);
        issue.IssueNumber = input.IssueNumber.Trim();
        issue.IssueDate = input.IssueDate.Date;
        issue.ReceivedBy = string.Empty;
        issue.ReceivedUnit = string.Empty;
        issue.DeliveredBy = string.Empty;
        issue.Reason = input.Reason?.Trim() ?? string.Empty;

        for (var i = 0; i < resolved.Count; i++)
        {
            var line = resolved[i];
            var issueLine = new IssueLine
            {
                ItemId = line.Item.Id,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = line.Quantity * line.UnitPrice,
                Note = line.Note
            };

            AddAllocationsToIssueLine(issueLine, line.Allocations);

            issue.Lines.Add(issueLine);
        }

        issue.TotalAmount = issue.Lines.Sum(x => x.Amount);
        issue.TotalAmountText = VietnameseMoneyTextConverter.Convert(issue.TotalAmount);

        foreach (var line in resolved.Where(x => x.Quantity > 0))
        {
            var period = await EnsurePeriodAsync(db, line.Item.Id, input.IssueDate.Year, line.UnitPrice);
            period.IssuedQuantity += line.Quantity;
            period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
            period.UnitPrice = line.UnitPrice;
            impactedPeriods.Add((line.Item.Id, input.IssueDate.Year));

            db.StockLedgerEntries.Add(new StockLedgerEntry
            {
                ItemId = line.Item.Id,
                TransactionDate = input.IssueDate.Date,
                TransactionType = "Issue",
                DocumentNumber = input.IssueNumber.Trim(),
                QuantityIn = 0,
                QuantityOut = line.Quantity,
                BalanceAfter = 0,
                UnitPrice = line.UnitPrice,
                Amount = line.Quantity * line.UnitPrice
            });
        }

        await RebuildLedgerBalancesAsync(db, impactedPeriods);
        await db.SaveChangesAsync();

        await SyncUnitIssuesInDbAsync(db, issue);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return issue;
    }

    public async Task DeleteReceiptAsync(int receiptId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var receipt = await db.Receipts.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == receiptId)
            ?? throw new InvalidOperationException("Không tìm thấy phiếu nhập cần xóa.");

        var itemIdsOnReceipt = receipt.Lines.Select(x => x.ItemId).Distinct().ToList();
        var impactedPeriods = new HashSet<(int ItemId, int Year)>();
        foreach (var line in receipt.Lines)
        {
            var period = await EnsurePeriodAsync(db, line.ItemId, receipt.ReceiptDate.Year, line.UnitPrice);
            if (period.ReceivedQuantity < line.Quantity)
            {
                throw new InvalidOperationException("Dữ liệu tồn kho không hợp lệ để xóa phiếu nhập.");
            }

            period.ReceivedQuantity -= line.Quantity;
            period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
            impactedPeriods.Add((line.ItemId, receipt.ReceiptDate.Year));
        }

        var ledgers = await db.StockLedgerEntries
            .Where(x => x.TransactionType == "Receipt" && x.DocumentNumber == receipt.ReceiptNumber)
            .ToListAsync();
        db.StockLedgerEntries.RemoveRange(ledgers);
        db.Receipts.Remove(receipt);

        await RebuildLedgerBalancesAsync(db, impactedPeriods);
        await db.SaveChangesAsync();

        // Xóa mặt hàng chỉ được tạo/dùng bởi phiếu này (không còn phiếu, sổ, tồn đầu).
        await RemoveUnusedItemsAsync(db, itemIdsOnReceipt);
        await db.SaveChangesAsync();

        // Mặt hàng còn lại: dựng lại ghi chú từ phiếu/tồn đầu còn hiệu lực (giữ nếu đang khóa Dashboard).
        var survivors = await db.Items.Where(x => itemIdsOnReceipt.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        await RefreshCatalogNotesForItemsAsync(db, survivors, receipt.ReceiptDate.Year);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// Xóa mặt hàng không còn tham chiếu phiếu nhập/xuất/sổ và không còn số liệu tồn kỳ.
    /// </summary>
    private static async Task RemoveUnusedItemsAsync(InventoryDbContext db, IReadOnlyCollection<int> candidateItemIds)
    {
        var ids = candidateItemIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        foreach (var itemId in ids)
        {
            var usedInReceipt = await db.ReceiptLines.AnyAsync(x => x.ItemId == itemId);
            if (usedInReceipt)
            {
                continue;
            }

            var usedInIssue = await db.IssueLines.AnyAsync(x => x.ItemId == itemId);
            if (usedInIssue)
            {
                continue;
            }

            var hasLedger = await db.StockLedgerEntries.AnyAsync(x => x.ItemId == itemId);
            if (hasLedger)
            {
                continue;
            }

            var periods = await db.StockPeriods.Where(x => x.ItemId == itemId).ToListAsync();
            if (periods.Any(p =>
                    p.OpeningQuantity != 0
                    || p.ReceivedQuantity != 0
                    || p.IssuedQuantity != 0
                    || p.ClosingQuantity != 0))
            {
                continue;
            }

            if (periods.Count > 0)
            {
                db.StockPeriods.RemoveRange(periods);
            }

            var item = await db.Items.SingleOrDefaultAsync(x => x.Id == itemId);
            if (item is not null)
            {
                db.Items.Remove(item);
            }
        }
    }

    public async Task DeleteIssueAsync(int issueId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var issue = await db.Issues.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == issueId)
            ?? throw new InvalidOperationException("Không tìm thấy phiếu cân đối cần xóa.");

        if (issue.ParentIssueId is not null)
        {
            throw new InvalidOperationException("Không thể xóa phiếu xuất đã sinh riêng. Hãy xóa phiếu cân đối hoặc sinh lại từ phiếu cân đối.");
        }

        // Xóa phiếu xuất theo đơn vị đã sinh (không trừ tồn).
        var children = await db.Issues.Include(x => x.Lines).Where(x => x.ParentIssueId == issueId).ToListAsync();
        if (children.Count > 0)
        {
            var childNumbers = children.Select(x => x.IssueNumber).Distinct().ToList();
            var childLedgers = await db.StockLedgerEntries
                .Where(x => x.TransactionType == "Issue" && childNumbers.Contains(x.DocumentNumber))
                .ToListAsync();
            if (childLedgers.Count > 0)
            {
                db.StockLedgerEntries.RemoveRange(childLedgers);
            }

            var childLineIds = children.SelectMany(c => c.Lines.Select(l => l.Id)).ToList();
            if (childLineIds.Count > 0)
            {
                var childAlloc = await db.IssueLineAllocations.Where(x => childLineIds.Contains(x.IssueLineId)).ToListAsync();
                db.IssueLineAllocations.RemoveRange(childAlloc);
            }

            db.Issues.RemoveRange(children);
        }

        var impactedPeriods = new HashSet<(int ItemId, int Year)>();
        foreach (var line in issue.Lines)
        {
            var period = await EnsurePeriodAsync(db, line.ItemId, issue.IssueDate.Year, line.UnitPrice);
            if (period.IssuedQuantity < line.Quantity)
            {
                throw new InvalidOperationException("Dữ liệu tồn kho không hợp lệ để xóa phiếu cân đối.");
            }

            period.IssuedQuantity -= line.Quantity;
            period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
            impactedPeriods.Add((line.ItemId, issue.IssueDate.Year));
        }

        var ledgers = await db.StockLedgerEntries
            .Where(x => x.TransactionType == "Issue" && x.DocumentNumber == issue.IssueNumber)
            .ToListAsync();
        db.StockLedgerEntries.RemoveRange(ledgers);
        db.Issues.Remove(issue);

        await RebuildLedgerBalancesAsync(db, impactedPeriods);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static async Task<List<ResolvedLine>> ResolveLinesAsync(
        InventoryDbContext db,
        IReadOnlyCollection<TransactionLineInput> inputs,
        bool allowCreateMissingItems,
        bool allowZeroQuantity = false)
    {
        var prepared = inputs
            .Where(x => !string.IsNullOrWhiteSpace(x.ItemCode) || !string.IsNullOrWhiteSpace(x.ItemName))
            .Select(x =>
            {
                var code = x.ItemCode?.Trim() ?? string.Empty;
                var name = x.ItemName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code))
                {
                    name = ParseItemCode(code).ItemName;
                }

                return new TransactionLineInput
                {
                    ItemCode = code,
                    ItemName = name,
                    Category = x.Category?.Trim() ?? string.Empty,
                    Unit = x.Unit?.Trim() ?? string.Empty,
                    UsageType = x.UsageType?.Trim() ?? string.Empty,
                    Quantity = x.Quantity,
                    UnitPrice = x.UnitPrice,
                    Note = x.Note,
                    Allocations = x.Allocations
                };
            })
            .ToList();

        var grouped = prepared
            .GroupBy(x => BuildLineGroupKey(x), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var allocations = new Dictionary<int, decimal>();
                foreach (var input in group)
                {
                    foreach (var (unitId, qty) in input.Allocations)
                    {
                        if (qty <= 0)
                        {
                            continue;
                        }

                        allocations[unitId] = allocations.GetValueOrDefault(unitId) + qty;
                    }
                }

                var last = group.Last();
                return new TransactionLineInput
                {
                    ItemCode = group.Select(x => x.ItemCode).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty,
                    ItemName = group.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.ItemName))?.ItemName?.Trim()
                               ?? last.ItemName,
                    Category = group.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.Category))?.Category?.Trim() ?? string.Empty,
                    Unit = group.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.Unit))?.Unit?.Trim() ?? string.Empty,
                    UsageType = group.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.UsageType))?.UsageType?.Trim() ?? string.Empty,
                    Quantity = group.Sum(x => x.Quantity),
                    UnitPrice = group.LastOrDefault(x => x.UnitPrice.HasValue && x.UnitPrice >= 0)?.UnitPrice,
                    Note = string.Join("; ", group.Select(x => x.Note).Where(x => !string.IsNullOrWhiteSpace(x))),
                    Allocations = allocations
                };
            })
            .ToList();

        var codeSet = grouped
            .Select(x => x.ItemCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var items = codeSet.Count == 0
            ? []
            : await db.Items.Where(x => codeSet.Contains(x.ItemCode)).ToListAsync();

        var resolved = new List<ResolvedLine>();
        foreach (var line in grouped)
        {
            var label = !string.IsNullOrWhiteSpace(line.ItemCode) ? line.ItemCode : line.ItemName;
            if (line.Quantity < 0 || (!allowZeroQuantity && line.Quantity <= 0))
            {
                throw new InvalidOperationException($"Số lượng không hợp lệ: {label}.");
            }

            Item? item = null;
            if (!string.IsNullOrWhiteSpace(line.ItemCode))
            {
                item = items.SingleOrDefault(x => x.ItemCode.Equals(line.ItemCode, StringComparison.OrdinalIgnoreCase));
            }

            var unitPrice = line.UnitPrice ?? item?.UnitPrice;
            if (item is null)
            {
                var (itemNameFromCode, unitPriceFromCode) = ParseItemCode(line.ItemCode);
                var finalUnitPrice = line.UnitPrice ?? unitPriceFromCode;
                var finalItemName = !string.IsNullOrWhiteSpace(line.ItemName)
                    ? line.ItemName.Trim()
                    : itemNameFromCode;

                if (finalUnitPrice.HasValue && finalUnitPrice.Value >= 0 && !string.IsNullOrWhiteSpace(finalItemName))
                {
                    var category = ItemVariantKey.NormalizeCategory(line.Category);
                    var usageType = ItemVariantKey.NormalizeUsageType(line.UsageType);
                    // So khớp 4 khóa không phân biệt hoa/thường (tránh UNIQUE ItemCode khi tạo trùng).
                    var duplicatePair = await FindMatchingVariantItemAsync(
                        db, finalItemName, finalUnitPrice.Value, category, usageType);
                    if (duplicatePair is not null)
                    {
                        item = duplicatePair;
                        unitPrice = finalUnitPrice;
                        if (!items.Any(x => x.Id == item.Id))
                        {
                            items.Add(item);
                        }
                    }
                }

                if (item is null)
                {
                    if (!allowCreateMissingItems)
                    {
                        throw new InvalidOperationException($"Không tìm thấy mã hàng: {label}.");
                    }

                    if (!finalUnitPrice.HasValue || finalUnitPrice.Value < 0)
                    {
                        throw new InvalidOperationException($"Đơn giá không hợp lệ để tạo mặt hàng mới: {label}.");
                    }

                    if (string.IsNullOrWhiteSpace(finalItemName))
                    {
                        throw new InvalidOperationException($"Thiếu tên mặt hàng để tạo mới (dòng: {label}).");
                    }

                    var category = ItemVariantKey.NormalizeCategory(line.Category);
                    var usageType = ItemVariantKey.NormalizeUsageType(line.UsageType);
                    // Tự tạo mã nếu chưa có / mã gõ vào chưa tồn tại.
                    var autoCode = await GenerateUniqueItemCodeAsync(
                        db, finalItemName, finalUnitPrice.Value, category, usageType);
                    // An toàn: nếu mã vừa sinh trùng biến thể đã có (race / hoa thường) thì tái sử dụng.
                    var byCode = await db.Items.SingleOrDefaultAsync(x => x.ItemCode == autoCode);
                    if (byCode is not null
                        && ItemVariantKey.Matches(byCode, finalItemName, finalUnitPrice.Value, category, usageType))
                    {
                        item = byCode;
                        unitPrice = finalUnitPrice;
                        if (!items.Any(x => x.Id == item.Id))
                        {
                            items.Add(item);
                        }
                    }
                    else if (byCode is not null)
                    {
                        throw new InvalidOperationException(
                            $"Không thể tạo mặt hàng mới vì mã '{autoCode}' đã tồn tại với biến thể khác.");
                    }
                    else
                    {
                        item = new Item
                        {
                            ItemCode = autoCode,
                            ItemName = finalItemName,
                            Category = category,
                            Unit = line.Unit.Trim(),
                            UsageType = usageType,
                            UnitPrice = finalUnitPrice.Value
                        };
                        db.Items.Add(item);
                        await db.SaveChangesAsync();
                        unitPrice = finalUnitPrice;
                        items.Add(item);
                    }
                }
            }
            // Mặt hàng đã có: không ghi đè danh mục / ĐVT / loại từ dòng phiếu.

            unitPrice ??= item.UnitPrice;
            if (unitPrice < 0)
            {
                throw new InvalidOperationException($"Đơn giá không hợp lệ (không được âm): {item.ItemCode}.");
            }

            var allocations = line.Allocations.Count > 0 ? line.Allocations : null;
            if (allocations is not null)
            {
                var allocationSum = allocations.Values.Sum();
                if (allocationSum != line.Quantity)
                {
                    throw new InvalidOperationException(
                        $"Tổng phân bổ đơn vị trực thuộc ({allocationSum:N0}) không khớp số lượng dòng ({line.Quantity:N0}) cho mặt hàng {item.ItemCode}.");
                }
            }

            resolved.Add(new ResolvedLine(item, line.Quantity, unitPrice.Value, line.Note, allocations));
        }

        return resolved;
    }

    private static string BuildLineGroupKey(TransactionLineInput x)
    {
        if (!string.IsNullOrWhiteSpace(x.ItemCode))
        {
            return "C:" + x.ItemCode.Trim().ToUpperInvariant();
        }

        var name = (x.ItemName ?? string.Empty).Trim().ToUpperInvariant();
        var price = x.UnitPrice ?? -1m;
        return $"N:{name}::{price.ToString(CultureInfo.InvariantCulture)}";
    }

    private static void AddAllocationsToIssueLine(IssueLine issueLine, Dictionary<int, decimal>? allocations)
    {
        if (allocations is not { Count: > 0 })
        {
            return;
        }

        foreach (var (unitId, qty) in allocations)
        {
            if (qty > 0)
            {
                issueLine.Allocations.Add(new IssueLineAllocation
                {
                    SubordinateUnitId = unitId,
                    Quantity = qty
                });
            }
        }
    }

    private static void ValidateReceiptInput(ReceiptInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ReceiptNumber)
            || string.IsNullOrWhiteSpace(input.DeliveredBy)
            || string.IsNullOrWhiteSpace(input.DeliveredUnit)
            || string.IsNullOrWhiteSpace(input.ReceivedBy)
            || string.IsNullOrWhiteSpace(input.Reason))
        {
            throw new InvalidOperationException("Thông tin phiếu nhập bắt buộc chưa đầy đủ.");
        }

        if (input.Lines.Count == 0)
        {
            throw new InvalidOperationException("Phiếu nhập phải có ít nhất 1 dòng hàng.");
        }
    }

    private static void ValidateIssueInput(IssueInput input)
    {
        if (string.IsNullOrWhiteSpace(input.IssueNumber))
        {
            throw new InvalidOperationException("Vui lòng nhập tên / số phiếu cân đối.");
        }

        if (input.Lines.Count == 0)
        {
            throw new InvalidOperationException("Phiếu cân đối phải có ít nhất 1 dòng hàng.");
        }
    }

    private static async Task<StockPeriod> EnsurePeriodAsync(InventoryDbContext db, int itemId, int year, decimal unitPrice)
    {
        var map = await EnsurePeriodsAsync(db, [(itemId, year)], unitPrice);
        return map[(itemId, year)];
    }

    private static async Task<Dictionary<(int ItemId, int Year), StockPeriod>> EnsurePeriodsAsync(
        InventoryDbContext db,
        IReadOnlyCollection<(int ItemId, int Year)> keys,
        decimal defaultUnitPrice = 0)
    {
        var distinct = keys.Distinct().ToList();
        var result = new Dictionary<(int ItemId, int Year), StockPeriod>();
        if (distinct.Count == 0)
        {
            return result;
        }

        var itemIds = distinct.Select(x => x.ItemId).Distinct().ToList();
        var years = distinct.Select(x => x.Year).Distinct().ToList();
        var existing = await db.StockPeriods
            .Where(x => itemIds.Contains(x.ItemId) && years.Contains(x.Year))
            .ToListAsync();

        foreach (var period in existing)
        {
            result[(period.ItemId, period.Year)] = period;
        }

        foreach (var key in distinct)
        {
            if (result.ContainsKey(key))
            {
                continue;
            }

            var period = new StockPeriod
            {
                ItemId = key.ItemId,
                Year = key.Year,
                OpeningQuantity = 0,
                ReceivedQuantity = 0,
                IssuedQuantity = 0,
                ClosingQuantity = 0,
                UnitPrice = defaultUnitPrice,
                IsManualAdjustment = false
            };
            db.StockPeriods.Add(period);
            result[key] = period;
        }

        return result;
    }

    private static async Task RebuildLedgerBalancesAsync(
        InventoryDbContext db,
        IReadOnlyCollection<(int ItemId, int Year)> impactedPeriods)
    {
        var keys = impactedPeriods.Distinct().ToList();
        if (keys.Count == 0)
        {
            return;
        }

        var periods = await EnsurePeriodsAsync(db, keys);
        var itemIds = keys.Select(x => x.ItemId).Distinct().ToList();
        var minYear = keys.Min(x => x.Year);
        var maxYear = keys.Max(x => x.Year);
        var rangeStart = new DateTime(minYear, 1, 1);
        var rangeEnd = new DateTime(maxYear, 12, 31);

        var allEntries = await db.StockLedgerEntries
            .Where(x => itemIds.Contains(x.ItemId)
                && x.TransactionDate >= rangeStart
                && x.TransactionDate <= rangeEnd)
            .OrderBy(x => x.ItemId)
            .ThenBy(x => x.TransactionDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var entriesByItemYear = allEntries
            .GroupBy(x => (x.ItemId, x.TransactionDate.Year))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var key in keys)
        {
            var period = periods[key];
            if (!entriesByItemYear.TryGetValue(key, out var entries))
            {
                period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
                continue;
            }

            var running = period.OpeningQuantity;
            foreach (var entry in entries)
            {
                running += entry.QuantityIn - entry.QuantityOut;
                entry.BalanceAfter = running;
            }

            period.ClosingQuantity = period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity;
        }
    }

    /// <summary>Tìm mặt hàng khớp đủ 4 khóa biến thể (không phân biệt hoa/thường tên/danh mục).</summary>
    private static async Task<Item?> FindMatchingVariantItemAsync(
        InventoryDbContext db,
        string itemName,
        decimal unitPrice,
        string category,
        string usageType)
    {
        var candidates = await db.Items
            .Where(x => x.UnitPrice == unitPrice)
            .ToListAsync();
        return candidates.FirstOrDefault(x =>
            ItemVariantKey.Matches(x, itemName, unitPrice, category, usageType));
    }

    private static async Task<string> GenerateUniqueItemCodeAsync(
        InventoryDbContext db,
        string itemName,
        decimal unitPrice,
        string? category = null,
        string? usageType = null)
    {
        // Mặc định giữ Tên:Giá; chỉ gắn nhãn khi mã gốc đã bị biến thể khác chiếm.
        var baseCode = ItemVariantKey.BuildBaseItemCode(itemName, unitPrice);
        var existed = await db.Items.SingleOrDefaultAsync(x => x.ItemCode == baseCode);
        if (existed is null)
        {
            return baseCode;
        }

        if (ItemVariantKey.Matches(existed, itemName, unitPrice, category, usageType))
        {
            return baseCode;
        }

        var variantCode = ItemVariantKey.BuildVariantItemCode(itemName, unitPrice, category, usageType);
        var stem = !string.IsNullOrEmpty(variantCode) ? variantCode : baseCode;
        if (!string.IsNullOrEmpty(variantCode)
            && !await db.Items.AnyAsync(x => x.ItemCode == variantCode))
        {
            return variantCode;
        }

        // Mã đã gắn nhãn vẫn trùng (hoặc không có nhãn) → hậu tố số.
        for (var suffix = 2; suffix <= 999; suffix++)
        {
            var suffixText = $"-{suffix}";
            var maxBase = Math.Max(1, 50 - suffixText.Length);
            var candidate = (stem.Length <= maxBase ? stem : stem[..maxBase]) + suffixText;
            if (!await db.Items.AnyAsync(x => x.ItemCode == candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Không thể tự sinh mã từ '{baseCode}' vì đã hết hậu tố khả dụng.");
    }

    private static (string ItemName, decimal? UnitPrice) ParseItemCode(string itemCode)
    {
        var trimmed = itemCode.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return (string.Empty, null);
        }

        var parts = trimmed.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return (trimmed, null);
        }

        var itemName = string.Join(":", parts[..^1]).Trim();
        // Hỗ trợ mã biến thể: 10000|VP|TC và mã cũ 10000-2.
        var priceToken = parts[^1];
        var pipe = priceToken.IndexOf('|');
        if (pipe >= 0)
        {
            priceToken = priceToken[..pipe];
        }

        var dash = priceToken.LastIndexOf('-');
        if (dash > 0
            && dash < priceToken.Length - 1
            && priceToken[(dash + 1)..].All(char.IsDigit)
            && priceToken[..dash].All(static c => char.IsDigit(c) || c == '.'))
        {
            priceToken = priceToken[..dash];
        }

        decimal? price = null;
        if (decimal.TryParse(priceToken, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantPrice))
        {
            price = invariantPrice;
        }
        else if (decimal.TryParse(priceToken, NumberStyles.Number, CultureInfo.CurrentCulture, out var localPrice))
        {
            price = localPrice;
        }

        return (itemName, price);
    }

    private static bool IsUpperLevelType(string? usageType)
    {
        if (string.IsNullOrWhiteSpace(usageType))
        {
            return false;
        }

        var normalized = usageType.Trim();
        return normalized.Equals("trên cấp", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("trên cap", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PopulateReceiptTypeAmountsAsync(InventoryDbContext db, IReadOnlyCollection<Receipt> receipts)
    {
        if (receipts.Count == 0)
        {
            return;
        }

        var receiptIds = receipts.Select(x => x.Id).ToList();
        var rows = await (
            from line in db.ReceiptLines
            join item in db.Items on line.ItemId equals item.Id
            where receiptIds.Contains(line.ReceiptId)
            select new { line.ReceiptId, line.Amount, item.UsageType }
        ).ToListAsync();

        var grouped = rows.GroupBy(x => x.ReceiptId).ToDictionary(
            g => g.Key,
            g => new
            {
                Upper = g.Where(x => IsUpperLevelType(x.UsageType)).Sum(x => x.Amount),
                Self = g.Where(x => !IsUpperLevelType(x.UsageType)).Sum(x => x.Amount)
            });

        foreach (var receipt in receipts)
        {
            if (grouped.TryGetValue(receipt.Id, out var split))
            {
                receipt.UpperLevelAmount = split.Upper;
                receipt.SelfProcuredAmount = split.Self;
            }
            else
            {
                receipt.UpperLevelAmount = 0;
                receipt.SelfProcuredAmount = 0;
            }
        }
    }

    private static async Task PopulateIssueTypeAmountsAsync(InventoryDbContext db, IReadOnlyCollection<Issue> issues)
    {
        if (issues.Count == 0)
        {
            return;
        }

        var issueIds = issues.Select(x => x.Id).ToList();
        var rows = await (
            from line in db.IssueLines
            join item in db.Items on line.ItemId equals item.Id
            where issueIds.Contains(line.IssueId)
            select new { line.IssueId, line.Amount, item.UsageType }
        ).ToListAsync();

        var grouped = rows.GroupBy(x => x.IssueId).ToDictionary(
            g => g.Key,
            g => new
            {
                Upper = g.Where(x => IsUpperLevelType(x.UsageType)).Sum(x => x.Amount),
                Self = g.Where(x => !IsUpperLevelType(x.UsageType)).Sum(x => x.Amount)
            });

        foreach (var issue in issues)
        {
            if (grouped.TryGetValue(issue.Id, out var split))
            {
                issue.UpperLevelAmount = split.Upper;
                issue.SelfProcuredAmount = split.Self;
            }
            else
            {
                issue.UpperLevelAmount = 0;
                issue.SelfProcuredAmount = 0;
            }
        }
    }

    /// <summary>
    /// Đồng bộ ghi chú mã hàng từ dòng phiếu nhập.
    /// - Có ghi chú trên phiếu → gắn vào mã hàng (trừ khi Dashboard đang khóa và ngày phiếu trước ngày khóa).
    /// - Trống trên phiếu + chưa khóa Dashboard → lấy fallback từ phiếu khác / tồn đầu (không xóa nhầm khi còn nguồn khác).
    /// - Trống trên phiếu + đang mở khóa sau sửa Dashboard → lấy đúng ghi chú phiếu (có thể xóa).
    /// </summary>
    private static async Task ApplyCatalogNotesFromReceiptLinesAsync(
        InventoryDbContext db,
        IEnumerable<ResolvedLine> lines,
        DateTime receiptDate,
        int? excludeReceiptId = null)
    {
        foreach (var group in lines.GroupBy(x => x.Item.Id))
        {
            var item = group.First().Item;
            var lineNote = group
                .Select(x => x.Note)
                .LastOrDefault(n => !string.IsNullOrWhiteSpace(n));

            if (item.NoteManualAt is { } manualAt && receiptDate.Date < manualAt.Date)
            {
                continue;
            }

            var wasManual = item.NoteManualAt is not null;
            if (!string.IsNullOrWhiteSpace(lineNote))
            {
                item.Note = lineNote.Trim();
                item.NoteManualAt = null;
                continue;
            }

            if (wasManual)
            {
                // Phiếu sau ngày sửa Dashboard không có ghi chú → xóa ghi chú gắn tay.
                item.Note = null;
                item.NoteManualAt = null;
                continue;
            }

            // Chưa khóa: trống trên phiếu này → nguồn khác (bỏ qua chính phiếu đang sửa vì dòng cũ chưa SaveChanges).
            item.Note = await ResolveCatalogNoteFallbackAsync(db, item.Id, receiptDate.Year, excludeReceiptId);
            item.NoteManualAt = null;
        }
    }

    private static async Task RefreshCatalogNotesForItemsAsync(
        InventoryDbContext db,
        IReadOnlyCollection<int> itemIds,
        int year)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        var items = await db.Items.Where(x => itemIds.Contains(x.Id)).ToListAsync();
        foreach (var item in items)
        {
            if (item.NoteManualAt is not null)
            {
                // Giữ ghi chú gắn tay trên Dashboard.
                continue;
            }

            item.Note = await ResolveCatalogNoteFallbackAsync(db, item.Id, year);
        }
    }

    private static async Task<string?> ResolveCatalogNoteFallbackAsync(
        InventoryDbContext db,
        int itemId,
        int year,
        int? excludeReceiptId = null)
    {
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = new DateTime(year, 12, 31);
        var fromReceipt = await (
            from line in db.ReceiptLines.AsNoTracking()
            join receipt in db.Receipts.AsNoTracking() on line.ReceiptId equals receipt.Id
            where line.ItemId == itemId
                  && receipt.ReceiptDate >= yearStart
                  && receipt.ReceiptDate <= yearEnd
                  && line.Note != null
                  && line.Note != ""
                  && (excludeReceiptId == null || receipt.Id != excludeReceiptId)
            orderby receipt.ReceiptDate descending, receipt.Id descending, line.Id descending
            select line.Note
        ).FirstOrDefaultAsync();

        if (!string.IsNullOrWhiteSpace(fromReceipt))
        {
            return fromReceipt.Trim();
        }

        var fromOpening = await db.StockPeriods.AsNoTracking()
            .Where(x => x.ItemId == itemId && x.Year == year && x.Note != null && x.Note != "")
            .Select(x => x.Note)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(fromOpening) ? null : fromOpening.Trim();
    }

    private static void TryApplyCatalogNoteFromSource(
        Item item,
        string? sourceNote,
        DateTime sourceMoment,
        bool syncEmpty)
    {
        if (item.NoteManualAt is { } manualAt && sourceMoment.Date < manualAt.Date)
        {
            return;
        }

        var newNote = string.IsNullOrWhiteSpace(sourceNote) ? null : sourceNote.Trim();
        if (newNote is null && !syncEmpty && item.NoteManualAt is null)
        {
            return;
        }

        item.Note = newNote;
        item.NoteManualAt = null;
    }

    private sealed record ResolvedLine(Item Item, decimal Quantity, decimal UnitPrice, string? Note, Dictionary<int, decimal>? Allocations = null);

    private async Task<string> GetDatabasePathAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var dataSource = db.Database.GetDbConnection().DataSource;
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new InvalidOperationException("Không xác định được đường dẫn cơ sở dữ liệu.");
        }

        return dataSource;
    }
}
