Option Explicit

Private Sub Workbook_SheetSelectionChange(ByVal Sh As Object, ByVal Target As Range)
    On Error Resume Next
    ZgsRememberSelection Sh, Target
End Sub

Private Sub Workbook_SheetBeforeDoubleClick(ByVal Sh As Object, ByVal Target As Range, Cancel As Boolean)
    On Error GoTo Failed
    ZgsDispatchAction Sh, Target, Cancel
    Exit Sub
Failed:
    MsgBox "配置操作失败：" & Err.Description, vbCritical
End Sub
