Option Explicit

Private mLastTableName As String
Private mLastRecordId As String

Public Sub ZgsRememberSelection(ByVal Sh As Object, ByVal Target As Range)
    Dim Table As ListObject
    Dim Hit As Range
    Dim Meta As Variant
    Dim RowIndex As Long
    Dim KeyColumn As Long

    If Not TypeOf Sh Is Worksheet Then Exit Sub
    For Each Table In Sh.ListObjects
        If Not Table.DataBodyRange Is Nothing Then
            Set Hit = Application.Intersect(Target, Table.DataBodyRange)
            If Not Hit Is Nothing Then
                Meta = ZgsTableMeta(Table.Name)
                KeyColumn = ZgsMachineColumn(Table, CStr(Meta(4)))
                RowIndex = Hit.Cells(1, 1).Row - Table.DataBodyRange.Row + 1
                mLastTableName = Table.Name
                mLastRecordId = ZgsText(Table.DataBodyRange.Cells(RowIndex, KeyColumn).Value2)
                Exit Sub
            End If
        End If
    Next Table
End Sub

Public Sub ZgsDispatchAction(ByVal Sh As Object, ByVal Target As Range, ByRef Cancel As Boolean)
    Dim DefinedName As Name
    Dim Hit As Range
    Dim ActionName As String

    For Each DefinedName In ThisWorkbook.Names
        If Left$(DefinedName.Name, 11) = "ZGS_ACTION_" Then
            On Error Resume Next
            Set Hit = DefinedName.RefersToRange
            On Error GoTo 0
            If Not Hit Is Nothing Then
                If Hit.Parent Is Sh Then
                    If Not Application.Intersect(Target, Hit) Is Nothing Then
                        Cancel = True
                        ActionName = Mid$(DefinedName.Name, InStrRev(DefinedName.Name, "_") + 1)
                        ZgsExecuteAction ActionName
                        Exit Sub
                    End If
                End If
            End If
            Set Hit = Nothing
        End If
    Next DefinedName
End Sub

Public Sub ZgsShortcutAdd()
    ZgsRunShortcut "ADD"
End Sub

Public Sub ZgsShortcutCopy()
    ZgsRunShortcut "COPY"
End Sub

Public Sub ZgsShortcutDelete()
    ZgsRunShortcut "DELETE"
End Sub

Public Sub ZgsShortcutRelation()
    ZgsRunShortcut "RELATION"
End Sub

Public Sub ZgsShortcutTechnical()
    ZgsRunShortcut "TECHNICAL"
End Sub

Public Sub ZgsShortcutHelp()
    ZgsRunShortcut "HELP"
End Sub

Private Sub ZgsRunShortcut(ByVal ActionName As String)
    On Error GoTo Failed
    ZgsExecuteAction ActionName
    Exit Sub
Failed:
    MsgBox "配置操作失败：" & Err.Description, vbCritical
End Sub

Private Sub ZgsExecuteAction(ByVal ActionName As String)
    Select Case UCase$(ActionName)
        Case "ADD": ZgsAddRecord
        Case "COPY": ZgsCopyRecord
        Case "DELETE": ZgsDeleteRecord
        Case "RELATION": ZgsEditRelation
        Case "TECHNICAL": ZgsToggleTechnical
        Case "HELP": ZgsShowHelp
        Case Else: Err.Raise vbObjectError + 820, "ZGS Authoring", "未知操作：" & ActionName
    End Select
End Sub

Private Sub ZgsAddRecord()
    Dim Table As ListObject
    Dim Meta As Variant
    Dim NewId As String
    Dim NewRow As ListRow
    Dim OrderField As String
    Dim EventsWereEnabled As Boolean

    Set Table = ZgsCurrentRootTable()
    Meta = ZgsTableMeta(Table.Name)
    If Not ZgsPromptText("新增配置", "请输入新的稳定 ID", "", NewId) Then Exit Sub
    NewId = Trim$(NewId)
    If Not ZgsValidId(NewId) Then
        MsgBox "稳定 ID 不能为空，也不能包含逗号、分号或换行。", vbExclamation
        Exit Sub
    End If
    If Not ZgsFindRow(Table, CStr(Meta(4)), NewId) Is Nothing Then
        MsgBox "稳定 ID 已存在：" & NewId, vbExclamation
        Exit Sub
    End If

    On Error GoTo Failed
    EventsWereEnabled = Application.EnableEvents
    Application.EnableEvents = False
    ZgsUnlock Table.Parent
    Set NewRow = Table.ListRows.Add
    ZgsSetValue NewRow, Table, CStr(Meta(4)), NewId, True
    OrderField = CStr(Meta(7))
    If Len(OrderField) = 0 Then OrderField = "order"
    ZgsSetValue NewRow, Table, OrderField, ZgsNextOrder(Table, OrderField), False
    mLastTableName = Table.Name
    mLastRecordId = NewId
    ZgsLock Table.Parent
    Application.EnableEvents = EventsWereEnabled
    MsgBox "已新增：" & NewId & "。请补齐必填字段，再回配置器生成预览。", vbInformation
    Exit Sub
Failed:
    On Error Resume Next
    If Not NewRow Is Nothing Then NewRow.Delete
    ZgsLock Table.Parent
    Application.EnableEvents = EventsWereEnabled
    MsgBox "新增失败，已撤销本次新增：" & Err.Description, vbCritical
End Sub

Private Sub ZgsCopyRecord()
    Dim Table As ListObject
    Dim Meta As Variant
    Dim SourceRow As ListRow
    Dim NewRow As ListRow
    Dim NewId As String
    Dim EventsWereEnabled As Boolean

    Set Table = ZgsCurrentRootTable()
    Meta = ZgsTableMeta(Table.Name)
    Set SourceRow = ZgsCurrentRow(Table, CStr(Meta(4)))
    If Not ZgsPromptText("复制配置", "请输入副本的稳定 ID", mLastRecordId & "-copy", NewId) Then Exit Sub
    NewId = Trim$(NewId)
    If Not ZgsValidId(NewId) Then
        MsgBox "稳定 ID 不合法。", vbExclamation
        Exit Sub
    End If
    If Not ZgsFindRow(Table, CStr(Meta(4)), NewId) Is Nothing Then
        MsgBox "稳定 ID 已存在：" & NewId, vbExclamation
        Exit Sub
    End If

    On Error GoTo Failed
    EventsWereEnabled = Application.EnableEvents
    Application.EnableEvents = False
    ZgsUnlockAll
    Set NewRow = Table.ListRows.Add
    NewRow.Range.Value2 = SourceRow.Range.Value2
    ZgsSetValue NewRow, Table, CStr(Meta(4)), NewId, True
    ZgsSetValue NewRow, Table, CStr(Meta(7)), ZgsNextOrder(Table, CStr(Meta(7))), False
    ZgsCopyChildren Table.Name, mLastRecordId, NewId
    mLastRecordId = NewId
    ZgsLockAll
    Application.EnableEvents = EventsWereEnabled
    MsgBox "已复制为：" & NewId, vbInformation
    Exit Sub
Failed:
    On Error Resume Next
    ZgsDeleteChildren Table.Name, NewId
    If Not NewRow Is Nothing Then NewRow.Delete
    ZgsLockAll
    Application.EnableEvents = EventsWereEnabled
    MsgBox "复制失败，已撤销副本：" & Err.Description, vbCritical
End Sub

Private Sub ZgsDeleteRecord()
    Dim Table As ListObject
    Dim Meta As Variant
    Dim CurrentRow As ListRow
    Dim Inbound As String
    Dim OwnedCount As Long
    Dim EventsWereEnabled As Boolean

    Set Table = ZgsCurrentRootTable()
    Meta = ZgsTableMeta(Table.Name)
    Set CurrentRow = ZgsCurrentRow(Table, CStr(Meta(4)))
    If ZgsIsProtected(Table.Name, mLastRecordId) Then
        MsgBox "这是受保护的关键记录，不能删除：" & mLastRecordId, vbExclamation
        Exit Sub
    End If
    Inbound = ZgsInboundReferences(Table.Name, CStr(Meta(1)), mLastRecordId)
    If Len(Inbound) > 0 Then
        MsgBox "该记录仍被独立数据引用，已阻止删除：" & vbCrLf & Inbound, vbExclamation
        Exit Sub
    End If
    OwnedCount = ZgsOwnedCount(Table.Name, mLastRecordId)
    If MsgBox("确认安全删除？" & vbCrLf & vbCrLf & _
        "稳定 ID：" & mLastRecordId & vbCrLf & _
        "同时清理自有关系行：" & CStr(OwnedCount), _
        vbQuestion + vbYesNo + vbDefaultButton2) <> vbYes Then Exit Sub

    On Error GoTo Failed
    EventsWereEnabled = Application.EnableEvents
    Application.EnableEvents = False
    ZgsUnlockAll
    ZgsDeleteChildren Table.Name, mLastRecordId
    CurrentRow.Delete
    mLastRecordId = ""
    ZgsLockAll
    Application.EnableEvents = EventsWereEnabled
    MsgBox "已安全删除。", vbInformation
    Exit Sub
Failed:
    ZgsLockAll
    Application.EnableEvents = EventsWereEnabled
    MsgBox "删除中断。请关闭工作簿并选择不保存，再重新检查。" & vbCrLf & Err.Description, vbCritical
End Sub

Private Sub ZgsEditRelation()
    Dim ParentTable As ListObject
    Dim ParentMeta As Variant
    Dim ChildTable As ListObject
    Dim ChildMeta As Variant
    Dim PayloadField As String
    Dim ReferencePath As String
    Dim Existing As String
    Dim Requested As String
    Dim Values As Collection

    Set ParentTable = ZgsCurrentRootTable()
    ParentMeta = ZgsTableMeta(ParentTable.Name)
    Set ChildTable = ZgsChooseRelationTable(ParentTable.Name, PayloadField, ReferencePath)
    If ChildTable Is Nothing Then Exit Sub
    ChildMeta = ZgsTableMeta(ChildTable.Name)
    Existing = ZgsOwnedValues(ChildTable, CStr(ChildMeta(6)), mLastRecordId, PayloadField)
    If Not ZgsPromptText("编辑关系", "输入完整的新列表，用英文逗号分隔。留空表示清空。" & vbCrLf & _
        ZgsAllowedHint(ReferencePath), Existing, Requested) Then Exit Sub
    Set Values = ZgsSplitValues(Requested)
    If Not ZgsValidateAllowed(Values, ReferencePath) Then Exit Sub

    On Error GoTo Failed
    ZgsUnlock ChildTable.Parent
    ZgsReplaceOwnedValues ChildTable, ChildMeta, mLastRecordId, PayloadField, Values
    ZgsLock ChildTable.Parent
    MsgBox "关系已更新。", vbInformation
    Exit Sub
Failed:
    ZgsLock ChildTable.Parent
    MsgBox "关系更新失败。请关闭工作簿并选择不保存。" & vbCrLf & Err.Description, vbCritical
End Sub

Private Sub ZgsToggleTechnical()
    Dim ParentTable As ListObject
    Dim DefinedName As Name
    Dim Meta As Variant
    Dim Table As ListObject
    Dim HideColumns As Boolean
    Dim HasTable As Boolean

    Set ParentTable = ZgsCurrentRootTable()
    ZgsUnlock ParentTable.Parent
    For Each DefinedName In ThisWorkbook.Names
        If Left$(DefinedName.Name, 15) = "ZGS_META_TABLE_" Then
            Meta = Split(ZgsDefinedValue(DefinedName), vbTab)
            If UBound(Meta) >= 5 Then
                If CStr(Meta(5)) = ParentTable.Name Then
                    Set Table = ZgsFindTable(CStr(Meta(0)))
                    If Not HasTable Then
                        HideColumns = Not Table.Range.Columns(1).EntireColumn.Hidden
                        HasTable = True
                    End If
                    Table.Range.EntireColumn.Hidden = HideColumns
                End If
            End If
        End If
    Next DefinedName
    ZgsLock ParentTable.Parent
    If Not HasTable Then MsgBox "当前配置没有独立技术关系区。", vbInformation
End Sub

Private Sub ZgsShowHelp()
    MsgBox "策划配置通用操作" & vbCrLf & vbCrLf & _
        "1. 先选中要操作的配置行。" & vbCrLf & _
        "2. 双击顶部操作，或使用括号内快捷键。" & vbCrLf & _
        "3. 新增/复制后补齐必填字段。" & vbCrLf & _
        "4. 删除会清理自有关系；独立引用会阻止删除。" & vbCrLf & _
        "5. 关键记录受保护，原生整行删除被锁定。" & vbCrLf & _
        "6. 保存后回 Unity 配置器生成预览；Excel 不生成 JSON。", _
        vbInformation, "ZGS 策划操作说明"
End Sub

Private Function ZgsCurrentRootTable() As ListObject
    Dim Table As ListObject
    Dim Meta As Variant
    If Len(mLastTableName) = 0 Then
        MsgBox "请先选中一条配置记录。", vbInformation
        Exit Function
    End If
    Set Table = ZgsFindTable(mLastTableName)
    Meta = ZgsTableMeta(Table.Name)
    If Len(CStr(Meta(5))) > 0 Then
        MsgBox "请选中左侧根配置记录；关系行请使用“编辑关系”。", vbInformation
        Exit Function
    End If
    Set ZgsCurrentRootTable = Table
End Function

Private Function ZgsCurrentRow(ByVal Table As ListObject, ByVal KeyField As String) As ListRow
    Set ZgsCurrentRow = ZgsFindRow(Table, KeyField, mLastRecordId)
    If ZgsCurrentRow Is Nothing Then
        Err.Raise vbObjectError + 821, "ZGS Authoring", "选中的记录已不存在，请重新选择。"
    End If
End Function

Private Function ZgsTableMeta(ByVal PhysicalName As String) As Variant
    Dim DefinedName As Name
    Dim Values As Variant
    For Each DefinedName In ThisWorkbook.Names
        If Left$(DefinedName.Name, 15) = "ZGS_META_TABLE_" Then
            Values = Split(ZgsDefinedValue(DefinedName), vbTab)
            If UBound(Values) >= 7 Then
                If CStr(Values(0)) = PhysicalName Then
                    ZgsTableMeta = Values
                    Exit Function
                End If
            End If
        End If
    Next DefinedName
    Err.Raise vbObjectError + 822, "ZGS Authoring", "缺少表元数据：" & PhysicalName
End Function

Private Function ZgsDefinedValue(ByVal DefinedName As Name) As String
    Dim Formula As String
    Formula = DefinedName.RefersTo
    If Left$(Formula, 2) = "=""" And Right$(Formula, 1) = """" Then
        Formula = Mid$(Formula, 3, Len(Formula) - 3)
        Formula = Replace(Formula, """""", """")
    End If
    ZgsDefinedValue = Formula
End Function

Private Function ZgsFindTable(ByVal PhysicalName As String) As ListObject
    Dim Sheet As Worksheet
    On Error Resume Next
    For Each Sheet In ThisWorkbook.Worksheets
        Set ZgsFindTable = Sheet.ListObjects(PhysicalName)
        If Not ZgsFindTable Is Nothing Then Exit Function
    Next Sheet
    On Error GoTo 0
    Err.Raise vbObjectError + 823, "ZGS Authoring", "找不到表：" & PhysicalName
End Function

Private Function ZgsFindRow(ByVal Table As ListObject, ByVal MachineName As String, _
    ByVal ExpectedValue As String) As ListRow
    Dim Row As ListRow
    Dim ColumnIndex As Long
    ColumnIndex = ZgsMachineColumn(Table, MachineName)
    For Each Row In Table.ListRows
        If ZgsText(Row.Range.Cells(1, ColumnIndex).Value2) = ExpectedValue Then
            Set ZgsFindRow = Row
            Exit Function
        End If
    Next Row
End Function

Private Function ZgsMachineColumn(ByVal Table As ListObject, ByVal MachineName As String) As Long
    Dim Index As Long
    For Index = 1 To Table.ListColumns.Count
        If ZgsMachineHeader(Table, Index) = MachineName Then
            ZgsMachineColumn = Index
            Exit Function
        End If
    Next Index
    Err.Raise vbObjectError + 824, "ZGS Authoring", _
        "表 " & Table.Name & " 缺少机器字段：" & MachineName
End Function

Private Function ZgsTryMachineColumn(ByVal Table As ListObject, ByVal MachineName As String) As Long
    Dim Index As Long
    For Index = 1 To Table.ListColumns.Count
        If ZgsMachineHeader(Table, Index) = MachineName Then
            ZgsTryMachineColumn = Index
            Exit Function
        End If
    Next Index
End Function

Private Function ZgsMachineHeader(ByVal Table As ListObject, ByVal ColumnIndex As Long) As String
    ZgsMachineHeader = ZgsText(Table.Parent.Cells(Table.HeaderRowRange.Row - 1, _
        Table.Range.Column + ColumnIndex - 1).Value2)
End Function

Private Sub ZgsSetValue(ByVal Row As ListRow, ByVal Table As ListObject, _
    ByVal MachineName As String, ByVal Value As Variant, ByVal Required As Boolean)
    Dim ColumnIndex As Long
    If Len(MachineName) = 0 Then Exit Sub
    ColumnIndex = ZgsTryMachineColumn(Table, MachineName)
    If ColumnIndex = 0 Then
        If Required Then Err.Raise vbObjectError + 825, "ZGS Authoring", "缺少字段：" & MachineName
        Exit Sub
    End If
    Row.Range.Cells(1, ColumnIndex).Value2 = Value
End Sub

Private Function ZgsNextOrder(ByVal Table As ListObject, ByVal OrderField As String) As Long
    Dim ColumnIndex As Long
    Dim Row As ListRow
    Dim Maximum As Long
    Dim Candidate As Variant
    If Len(OrderField) = 0 Then OrderField = "order"
    ColumnIndex = ZgsTryMachineColumn(Table, OrderField)
    If ColumnIndex = 0 Then Exit Function
    Maximum = -1
    For Each Row In Table.ListRows
        Candidate = Row.Range.Cells(1, ColumnIndex).Value2
        If IsNumeric(Candidate) Then If CLng(Candidate) > Maximum Then Maximum = CLng(Candidate)
    Next Row
    ZgsNextOrder = Maximum + 1
End Function

Private Sub ZgsCopyChildren(ByVal ParentPhysical As String, ByVal OldParentId As String, _
    ByVal NewParentId As String)
    Dim DefinedName As Name
    Dim Meta As Variant
    Dim Child As ListObject
    Dim SourceRows As New Collection
    Dim Row As ListRow
    Dim NewRow As ListRow
    Dim ParentColumn As Long
    Dim PrimaryColumn As Long
    Dim Sequence As Long
    Dim OldChildId As String
    Dim NewChildId As String

    For Each DefinedName In ThisWorkbook.Names
        If Left$(DefinedName.Name, 15) = "ZGS_META_TABLE_" Then
            Meta = Split(ZgsDefinedValue(DefinedName), vbTab)
            If UBound(Meta) >= 7 Then
                If CStr(Meta(5)) = ParentPhysical Then
                    Set Child = ZgsFindTable(CStr(Meta(0)))
                    ParentColumn = ZgsMachineColumn(Child, CStr(Meta(6)))
                    For Each Row In Child.ListRows
                        If ZgsText(Row.Range.Cells(1, ParentColumn).Value2) = OldParentId Then
                            SourceRows.Add Row
                        End If
                    Next Row
                    For Each Row In SourceRows
                        OldChildId = ZgsText(Row.Range.Cells(1, ZgsMachineColumn(Child, CStr(Meta(4)))).Value2)
                        Set NewRow = Child.ListRows.Add
                        NewRow.Range.Value2 = Row.Range.Value2
                        NewChildId = NewParentId & "-" & CStr(Meta(2)) & "-" & Format$(Sequence, "0000")
                        ZgsSetValue NewRow, Child, CStr(Meta(6)), NewParentId, True
                        ZgsSetValue NewRow, Child, CStr(Meta(4)), NewChildId, True
                        ZgsSetValue NewRow, Child, CStr(Meta(7)), Sequence, False
                        ZgsCopyChildren Child.Name, OldChildId, NewChildId
                        Sequence = Sequence + 1
                    Next Row
                    Set SourceRows = New Collection
                End If
            End If
        End If
    Next DefinedName
End Sub

Private Sub ZgsDeleteChildren(ByVal ParentPhysical As String, ByVal ParentId As String)
    Dim DefinedName As Name
    Dim Meta As Variant
    Dim Child As ListObject
    Dim RowIndex As Long
    Dim ParentColumn As Long
    Dim ChildId As String
    For Each DefinedName In ThisWorkbook.Names
        If Left$(DefinedName.Name, 15) = "ZGS_META_TABLE_" Then
            Meta = Split(ZgsDefinedValue(DefinedName), vbTab)
            If UBound(Meta) >= 7 Then
                If CStr(Meta(5)) = ParentPhysical Then
                    Set Child = ZgsFindTable(CStr(Meta(0)))
                    ParentColumn = ZgsMachineColumn(Child, CStr(Meta(6)))
                    For RowIndex = Child.ListRows.Count To 1 Step -1
                        If ZgsText(Child.ListRows(RowIndex).Range.Cells(1, ParentColumn).Value2) = ParentId Then
                            ChildId = ZgsText(Child.ListRows(RowIndex).Range.Cells(1, _
                                ZgsMachineColumn(Child, CStr(Meta(4)))).Value2)
                            ZgsDeleteChildren Child.Name, ChildId
                            Child.ListRows(RowIndex).Delete
                        End If
                    Next RowIndex
                End If
            End If
        End If
    Next DefinedName
End Sub

Private Function ZgsOwnedCount(ByVal ParentPhysical As String, ByVal ParentId As String) As Long
    Dim DefinedName As Name
    Dim Meta As Variant
    Dim Child As ListObject
    Dim Row As ListRow
    Dim ParentColumn As Long
    For Each DefinedName In ThisWorkbook.Names
        If Left$(DefinedName.Name, 15) = "ZGS_META_TABLE_" Then
            Meta = Split(ZgsDefinedValue(DefinedName), vbTab)
            If UBound(Meta) >= 7 Then
                If CStr(Meta(5)) = ParentPhysical Then
                    Set Child = ZgsFindTable(CStr(Meta(0)))
                    ParentColumn = ZgsMachineColumn(Child, CStr(Meta(6)))
                    For Each Row In Child.ListRows
                        If ZgsText(Row.Range.Cells(1, ParentColumn).Value2) = ParentId Then
                            ZgsOwnedCount = ZgsOwnedCount + 1
                        End If
                    Next Row
                End If
            End If
        End If
    Next DefinedName
End Function

Private Function ZgsIsProtected(ByVal PhysicalName As String, ByVal RecordId As String) As Boolean
    Dim DefinedName As Name
    Dim Values As Variant
    For Each DefinedName In ThisWorkbook.Names
        If Left$(DefinedName.Name, 19) = "ZGS_META_PROTECTED_" Then
            Values = Split(ZgsDefinedValue(DefinedName), vbTab)
            If UBound(Values) >= 1 Then
                If CStr(Values(0)) = PhysicalName And CStr(Values(1)) = RecordId Then
                    ZgsIsProtected = True
                    Exit Function
                End If
            End If
        End If
    Next DefinedName
End Function

Private Function ZgsInboundReferences(ByVal TargetPhysical As String, ByVal TargetRoot As String, _
    ByVal RecordId As String) As String
    Dim DefinedName As Name
    Dim FieldMeta As Variant
    Dim TableMeta As Variant
    Dim Source As ListObject
    Dim Row As ListRow
    Dim FieldColumn As Long
    Dim ParentColumn As Long
    Dim ParentId As String
    Dim Result As String
    Dim Count As Long

    For Each DefinedName In ThisWorkbook.Names
        If Left$(DefinedName.Name, 15) = "ZGS_META_FIELD_" Then
            FieldMeta = Split(ZgsDefinedValue(DefinedName), vbTab)
            If UBound(FieldMeta) >= 2 Then
                If ZgsReferenceRoot(CStr(FieldMeta(2))) = TargetRoot Then
                    Set Source = ZgsFindTable(CStr(FieldMeta(0)))
                    FieldColumn = ZgsMachineColumn(Source, CStr(FieldMeta(1)))
                    TableMeta = ZgsTableMeta(Source.Name)
                    ParentColumn = 0
                    If Len(CStr(TableMeta(5))) > 0 Then _
                        ParentColumn = ZgsMachineColumn(Source, CStr(TableMeta(6)))
                    For Each Row In Source.ListRows
                        If ZgsText(Row.Range.Cells(1, FieldColumn).Value2) = RecordId Then
                            ParentId = ""
                            If ParentColumn > 0 Then ParentId = ZgsText(Row.Range.Cells(1, ParentColumn).Value2)
                            If Not (CStr(TableMeta(5)) = TargetPhysical And ParentId = RecordId) Then
                                If Len(Result) > 0 Then Result = Result & vbCrLf
                                Result = Result & Source.Parent.Name & "/" & Source.Name & _
                                    IIf(Len(ParentId) > 0, " parentId=" & ParentId, "")
                                Count = Count + 1
                                If Count >= 12 Then GoTo Complete
                            End If
                        End If
                    Next Row
                End If
            End If
        End If
    Next DefinedName
Complete:
    ZgsInboundReferences = Result
End Function

Private Function ZgsReferenceRoot(ByVal ReferencePath As String) As String
    Const Prefix As String = "#/properties/"
    Dim Rest As String
    Dim Slash As Long
    If Left$(ReferencePath, Len(Prefix)) <> Prefix Then Exit Function
    Rest = Mid$(ReferencePath, Len(Prefix) + 1)
    Slash = InStr(Rest, "/")
    If Slash > 0 Then Rest = Left$(Rest, Slash - 1)
    ZgsReferenceRoot = Rest
End Function

Private Function ZgsChooseRelationTable(ByVal ParentPhysical As String, _
    ByRef PayloadField As String, ByRef ReferencePath As String) As ListObject
    Dim DefinedName As Name
    Dim Meta As Variant
    Dim Candidates As New Collection
    Dim Labels As String
    Dim Index As Long
    Dim Choice As String
    Dim Candidate As Variant
    Dim FieldInfo As Variant

    For Each DefinedName In ThisWorkbook.Names
        If Left$(DefinedName.Name, 15) = "ZGS_META_TABLE_" Then
            Meta = Split(ZgsDefinedValue(DefinedName), vbTab)
            If UBound(Meta) >= 7 Then
                If CStr(Meta(5)) = ParentPhysical Then
                    FieldInfo = ZgsSimplePayload(CStr(Meta(0)), CStr(Meta(4)), CStr(Meta(7)))
                    If CStr(FieldInfo(0)) <> "" Then
                        Candidates.Add Array(CStr(Meta(0)), CStr(Meta(2)), _
                            CStr(FieldInfo(0)), CStr(FieldInfo(1)))
                    End If
                End If
            End If
        End If
    Next DefinedName
    If Candidates.Count = 0 Then
        MsgBox "当前记录没有可用的简单关系编辑器。", vbInformation
        Exit Function
    End If
    If Candidates.Count = 1 Then
        Candidate = Candidates(1)
    Else
        For Index = 1 To Candidates.Count
            If Len(Labels) > 0 Then Labels = Labels & vbCrLf
            Labels = Labels & CStr(Index) & ". " & CStr(Candidates(Index)(1))
        Next Index
        If Not ZgsPromptText("编辑关系", "请选择关系序号：" & vbCrLf & Labels, "1", Choice) Then Exit Function
        If Not IsNumeric(Choice) Then Exit Function
        Index = CLng(Choice)
        If Index < 1 Or Index > Candidates.Count Then Exit Function
        Candidate = Candidates(Index)
    End If
    PayloadField = CStr(Candidate(2))
    ReferencePath = CStr(Candidate(3))
    Set ZgsChooseRelationTable = ZgsFindTable(CStr(Candidate(0)))
End Function

Private Function ZgsSimplePayload(ByVal PhysicalName As String, ByVal PrimaryField As String, _
    ByVal OrderField As String) As Variant
    Dim DefinedName As Name
    Dim Meta As Variant
    Dim FieldName As String
    Dim ReferencePath As String
    Dim Count As Long
    For Each DefinedName In ThisWorkbook.Names
        If Left$(DefinedName.Name, 15) = "ZGS_META_FIELD_" Then
            Meta = Split(ZgsDefinedValue(DefinedName), vbTab)
            If UBound(Meta) >= 4 Then
                If CStr(Meta(0)) = PhysicalName Then
                    FieldName = CStr(Meta(1))
                    If FieldName <> PrimaryField And FieldName <> OrderField And CStr(Meta(4)) <> "1" Then
                        Count = Count + 1
                        ZgsSimplePayload = Array(FieldName, CStr(Meta(2)))
                    End If
                End If
            End If
        End If
    Next DefinedName
    If Count <> 1 Then ZgsSimplePayload = Array("", "")
End Function

Private Function ZgsOwnedValues(ByVal Table As ListObject, ByVal ParentField As String, _
    ByVal ParentId As String, ByVal PayloadField As String) As String
    Dim Row As ListRow
    Dim ParentColumn As Long
    Dim PayloadColumn As Long
    ParentColumn = ZgsMachineColumn(Table, ParentField)
    PayloadColumn = ZgsMachineColumn(Table, PayloadField)
    For Each Row In Table.ListRows
        If ZgsText(Row.Range.Cells(1, ParentColumn).Value2) = ParentId Then
            If Len(ZgsOwnedValues) > 0 Then ZgsOwnedValues = ZgsOwnedValues & ","
            ZgsOwnedValues = ZgsOwnedValues & ZgsText(Row.Range.Cells(1, PayloadColumn).Value2)
        End If
    Next Row
End Function

Private Sub ZgsReplaceOwnedValues(ByVal Table As ListObject, ByVal Meta As Variant, _
    ByVal ParentId As String, ByVal PayloadField As String, ByVal Values As Collection)
    Dim RowIndex As Long
    Dim Row As ListRow
    Dim ParentColumn As Long
    Dim Value As Variant
    Dim Sequence As Long
    ParentColumn = ZgsMachineColumn(Table, CStr(Meta(6)))
    For RowIndex = Table.ListRows.Count To 1 Step -1
        If ZgsText(Table.ListRows(RowIndex).Range.Cells(1, ParentColumn).Value2) = ParentId Then _
            Table.ListRows(RowIndex).Delete
    Next RowIndex
    For Each Value In Values
        Set Row = Table.ListRows.Add
        ZgsSetValue Row, Table, CStr(Meta(6)), ParentId, True
        ZgsSetValue Row, Table, CStr(Meta(4)), _
            ParentId & "-" & CStr(Meta(2)) & "-" & Format$(Sequence, "0000"), True
        ZgsSetValue Row, Table, CStr(Meta(7)), Sequence, False
        ZgsSetValue Row, Table, PayloadField, CStr(Value), True
        Sequence = Sequence + 1
    Next Value
End Sub

Private Function ZgsSplitValues(ByVal Text As String) As Collection
    Dim Result As New Collection
    Dim Part As Variant
    Dim Value As String
    For Each Part In Split(Text, ",")
        Value = Trim$(CStr(Part))
        If Len(Value) > 0 Then If Not ZgsCollectionContains(Result, Value) Then Result.Add Value
    Next Part
    Set ZgsSplitValues = Result
End Function

Private Function ZgsValidateAllowed(ByVal Values As Collection, ByVal ReferencePath As String) As Boolean
    Dim Root As String
    Dim Table As ListObject
    Dim Meta As Variant
    Dim Value As Variant
    Root = ZgsReferenceRoot(ReferencePath)
    If Len(Root) = 0 Then
        ZgsValidateAllowed = True
        Exit Function
    End If
    Set Table = ZgsRootTable(Root)
    Meta = ZgsTableMeta(Table.Name)
    For Each Value In Values
        If ZgsFindRow(Table, CStr(Meta(4)), CStr(Value)) Is Nothing Then
            MsgBox "未登记的关系值：" & CStr(Value) & "。没有写入。", vbExclamation
            Exit Function
        End If
    Next Value
    ZgsValidateAllowed = True
End Function

Private Function ZgsAllowedHint(ByVal ReferencePath As String) As String
    Dim Root As String
    Dim Table As ListObject
    Dim Meta As Variant
    Dim Row As ListRow
    Dim Result As String
    Root = ZgsReferenceRoot(ReferencePath)
    If Len(Root) = 0 Then Exit Function
    Set Table = ZgsRootTable(Root)
    Meta = ZgsTableMeta(Table.Name)
    For Each Row In Table.ListRows
        If Len(Result) > 0 Then Result = Result & ", "
        Result = Result & ZgsText(Row.Range.Cells(1, ZgsMachineColumn(Table, CStr(Meta(4)))).Value2)
        If Len(Result) > 180 Then
            Result = Result & " …"
            Exit For
        End If
    Next Row
    ZgsAllowedHint = "可选值：" & Result
End Function

Private Function ZgsRootTable(ByVal RootProperty As String) As ListObject
    Dim DefinedName As Name
    Dim Meta As Variant
    For Each DefinedName In ThisWorkbook.Names
        If Left$(DefinedName.Name, 15) = "ZGS_META_TABLE_" Then
            Meta = Split(ZgsDefinedValue(DefinedName), vbTab)
            If UBound(Meta) >= 5 Then
                If CStr(Meta(1)) = RootProperty And Len(CStr(Meta(5))) = 0 Then
                    Set ZgsRootTable = ZgsFindTable(CStr(Meta(0)))
                    Exit Function
                End If
            End If
        End If
    Next DefinedName
End Function

Private Function ZgsCollectionContains(ByVal Values As Collection, ByVal Candidate As String) As Boolean
    Dim Value As Variant
    For Each Value In Values
        If CStr(Value) = Candidate Then
            ZgsCollectionContains = True
            Exit Function
        End If
    Next Value
End Function

Private Function ZgsPromptText(ByVal Title As String, ByVal Prompt As String, _
    ByVal DefaultValue As String, ByRef Value As String) As Boolean
    Dim Answer As Variant
    Answer = Application.InputBox(Prompt:=Prompt, Title:=Title, Default:=DefaultValue, Type:=2)
    If VarType(Answer) = vbBoolean Then If Answer = False Then Exit Function
    Value = CStr(Answer)
    ZgsPromptText = True
End Function

Private Function ZgsValidId(ByVal Value As String) As Boolean
    Value = Trim$(Value)
    If Len(Value) = 0 Then Exit Function
    If InStr(Value, ",") > 0 Or InStr(Value, ";") > 0 Then Exit Function
    If InStr(Value, vbCr) > 0 Or InStr(Value, vbLf) > 0 Then Exit Function
    ZgsValidId = True
End Function

Private Function ZgsText(ByVal Value As Variant) As String
    If IsError(Value) Or IsNull(Value) Or IsEmpty(Value) Then Exit Function
    ZgsText = Trim$(CStr(Value))
End Function

Private Sub ZgsUnlock(ByVal Sheet As Worksheet)
    Sheet.Unprotect
End Sub

Private Sub ZgsLock(ByVal Sheet As Worksheet)
    Sheet.Protect DrawingObjects:=True, Contents:=True, Scenarios:=True, _
        AllowFiltering:=True, AllowSorting:=True, UserInterfaceOnly:=True
End Sub

Private Sub ZgsUnlockAll()
    Dim Sheet As Worksheet
    For Each Sheet In ThisWorkbook.Worksheets
        If Sheet.Visible = xlSheetVisible Then ZgsUnlock Sheet
    Next Sheet
End Sub

Private Sub ZgsLockAll()
    Dim Sheet As Worksheet
    For Each Sheet In ThisWorkbook.Worksheets
        If Sheet.Visible = xlSheetVisible Then ZgsLock Sheet
    Next Sheet
End Sub
