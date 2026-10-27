Imports MySqlConnector

Public NotInheritable Class StockRepository
    Private Sub New()
    End Sub

    Public Shared Function Insert(conn As MySqlConnection, tx As MySqlTransaction, t As StockTransaction) As Integer
        Return Db.Insert(conn, tx,
            "INSERT INTO stock_transactions (item_id, item_name, txn_type, qty_change, qty_before, qty_after, " &
            "supplier_id, supplier_name, txn_date, reason, note) " &
            "VALUES (@item, @itemName, @type, @change, @before, @after, @supplier, @supplierName, @date, @reason, @note)",
            Db.P("@item", t.ItemId),
            Db.P("@itemName", t.ItemName),
            Db.P("@type", t.TxnType),
            Db.P("@change", t.QtyChange),
            Db.P("@before", t.QtyBefore),
            Db.P("@after", t.QtyAfter),
            Db.P("@supplier", t.SupplierId),
            Db.P("@supplierName", Db.NullIfBlank(t.SupplierName)),
            Db.P("@date", t.TxnDate.Date),
            Db.P("@reason", Db.NullIfBlank(t.Reason)),
            Db.P("@note", Db.NullIfBlank(t.Note)))
    End Function

    ''' <summary>Stock-in history (opening stock + deliveries) or adjustment history, newest first.</summary>
    Public Shared Function GetHistory(conn As MySqlConnection, adjustments As Boolean, limit As Integer) As List(Of StockTransaction)
        Dim filter = If(adjustments, "txn_type = 'ADJUSTMENT'", "txn_type IN ('INITIAL', 'STOCK_IN')")
        Return Db.Query(Of StockTransaction)(conn, Nothing,
            $"SELECT * FROM stock_transactions WHERE {filter} ORDER BY created_at DESC, id DESC LIMIT @limit",
            AddressOf Map, Db.P("@limit", limit))
    End Function

    ''' <summary>Every stock change whose transaction date falls within the range (both ends included), oldest first.</summary>
    Public Shared Function GetByDateRange(conn As MySqlConnection, fromDate As Date, toDate As Date) As List(Of StockTransaction)
        Return Db.Query(Of StockTransaction)(conn, Nothing,
            "SELECT * FROM stock_transactions WHERE txn_date BETWEEN @from AND @to ORDER BY txn_date, created_at, id",
            AddressOf Map, Db.P("@from", fromDate.Date), Db.P("@to", toDate.Date))
    End Function

    ''' <summary>Earliest and latest dates found in items and stock records; used for the "All time" report range.</summary>
    Public Shared Function GetDateBounds(conn As MySqlConnection) As (Earliest As Date?, Latest As Date?)
        Dim columns = {"added_min", "added_max", "expiry_min", "expiry_max", "txn_min", "txn_max"}
        Dim values = Db.Query(Of Date?())(conn, Nothing,
            "SELECT (SELECT MIN(created_at) FROM items) AS added_min, (SELECT MAX(created_at) FROM items) AS added_max, " &
            "(SELECT MIN(expiration_date) FROM items) AS expiry_min, (SELECT MAX(expiration_date) FROM items) AS expiry_max, " &
            "(SELECT MIN(txn_date) FROM stock_transactions) AS txn_min, (SELECT MAX(txn_date) FROM stock_transactions) AS txn_max",
            Function(r) columns.Select(Function(c) Db.GetNullableDate(r, c)).ToArray()).First()
        Dim found = values.Where(Function(v) v.HasValue).Select(Function(v) v.Value).ToList()
        If found.Count = 0 Then Return (Nothing, Nothing)
        Return (found.Min(), found.Max())
    End Function

    Public Shared Function Map(r As MySqlDataReader) As StockTransaction
        Return New StockTransaction With {
            .Id = Db.GetInt(r, "id"),
            .ItemId = Db.GetNullableInt(r, "item_id"),
            .ItemName = Db.GetString(r, "item_name"),
            .TxnType = Db.GetString(r, "txn_type"),
            .QtyChange = Db.GetDecimal(r, "qty_change"),
            .QtyBefore = Db.GetDecimal(r, "qty_before"),
            .QtyAfter = Db.GetDecimal(r, "qty_after"),
            .SupplierId = Db.GetNullableInt(r, "supplier_id"),
            .SupplierName = Db.GetString(r, "supplier_name"),
            .TxnDate = Db.GetDate(r, "txn_date"),
            .Reason = Db.GetString(r, "reason"),
            .Note = Db.GetString(r, "note"),
            .CreatedAt = Db.GetDate(r, "created_at")
        }
    End Function
End Class
