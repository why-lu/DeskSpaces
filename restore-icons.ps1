$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DeskSpacesIconRestore {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string n);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
'@
$progman = [DeskSpacesIconRestore]::FindWindow('Progman', [NullString]::Value)
if ($progman.Equals([IntPtr]::Zero)) { throw 'Windows Progman was not found.' }
$view = [DeskSpacesIconRestore]::FindWindowEx($progman, [IntPtr]::Zero, 'SHELLDLL_DefView', [NullString]::Value)
if ($view.Equals([IntPtr]::Zero)) {
    $worker = [IntPtr]::Zero
    while ($true) {
        $nextWorker = [DeskSpacesIconRestore]::FindWindowEx([IntPtr]::Zero, $worker, 'WorkerW', [NullString]::Value)
        if ($nextWorker.Equals([IntPtr]::Zero)) { break }
        $worker = $nextWorker
        $view = [DeskSpacesIconRestore]::FindWindowEx($worker, [IntPtr]::Zero, 'SHELLDLL_DefView', [NullString]::Value)
        if ($view -ne [IntPtr]::Zero) { break }
    }
}
if ($view.Equals([IntPtr]::Zero)) { throw 'Windows shell view was not found.' }
$list = [DeskSpacesIconRestore]::FindWindowEx($view, [IntPtr]::Zero, 'SysListView32', [NullString]::Value)
if ($list.Equals([IntPtr]::Zero)) { throw 'Windows icon list was not found.' }
[void][DeskSpacesIconRestore]::ShowWindow($list, 5)
Start-Sleep -Milliseconds 500
if (-not [DeskSpacesIconRestore]::IsWindowVisible($list)) { throw 'The icon list is still hidden.' }
Write-Output 'Windows desktop icons are visible.'
