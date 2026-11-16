# Build bản Release vào thư mục cài riêng (%LOCALAPPDATA%\Programs\TodayChecklist), không cần tắt app đang chạy.
#   deploy            → bản mới được dùng từ lần mở app sau
#   deploy -Restart   → đóng app đang chạy (lưu hết, như bấm X) rồi mở bản mới
# Mỗi lần build ra 1 thư mục app-<thời gian>; "current" là junction trỏ tới bản mới nhất, shortcut trỏ vào current.
param([switch]$Restart)
$ErrorActionPreference = 'Stop'

$root = Join-Path $env:LOCALAPPDATA 'Programs\TodayChecklist'
$current = Join-Path $root 'current'
$target = Join-Path $root ('app-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))

# Build ra obj\deploy thay vì bin\Release, để không đụng file của app nếu nó đang chạy từ bin\Release
$build = Join-Path $PSScriptRoot 'obj\deploy\'
dotnet publish (Join-Path $PSScriptRoot 'TodayChecklist.csproj') -c Release -o $target -p:OutDir=$build -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build lỗi; app đang chạy không bị ảnh hưởng.' }

# Đổi junction sang bản mới. App đang chạy giữ file trong thư mục bản cũ nên không bị khóa gì.
if (Test-Path $current) { cmd /c rmdir "$current" }   # rmdir chỉ gỡ junction, không xóa bản cũ
New-Item -ItemType Junction -Path $current -Target $target | Out-Null
"Bản mới: $target"

if ($Restart) {
    # Báo app đang dùng DB thật tự đóng (lưu hết như bấm X) qua tín hiệu riêng, vì app có thể đang ở khay hệ thống.
    # Tên tín hiệu giống Program.cs: "TodayChecklist-" + 16 ký tự đầu SHA-256 của đường dẫn DB (chữ thường) + "-quit".
    $db = [IO.Path]::Combine($env:LOCALAPPDATA, 'TodayChecklist', 'checklist.db').ToLowerInvariant()
    $hash = -join ([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($db)) | ForEach-Object { $_.ToString('X2') })
    $quit = $null
    if ([Threading.EventWaitHandle]::TryOpenExisting("TodayChecklist-$($hash.Substring(0, 16))-quit", [ref]$quit)) { [void]$quit.Set() }

    # Chỉ đợi bản dùng DB thật (chạy không kèm tham số); bản thử mở DB khác thì để yên
    $apps = Get-CimInstance Win32_Process -Filter "Name='TodayChecklist.exe'" | Where-Object { $_.CommandLine -notmatch '\.db' }
    foreach ($app in $apps) {
        $p = Get-Process -Id $app.ProcessId -ErrorAction SilentlyContinue
        if (-not $p) { continue }
        if (-not $p.WaitForExit(5000)) { [void]$p.CloseMainWindow() } # bản cũ chưa có tín hiệu thoát
        if (-not $p.WaitForExit(10000)) { throw 'App chưa đóng được (có thể đang mở hộp thoại). Đóng tay rồi chạy lại.' }
    }
    Start-Process (Join-Path $current 'TodayChecklist.exe')
    'Đã mở lại app bằng bản mới.'
}

# Dọn bản cũ: giữ 3 bản gần nhất; bản nào còn đang chạy (bị khóa) thì để lần sau dọn
Get-ChildItem $root -Directory -Filter 'app-*' | Sort-Object Name -Descending | Select-Object -Skip 3 |
    ForEach-Object { try { Remove-Item $_.FullName -Recurse -Force } catch { } }
