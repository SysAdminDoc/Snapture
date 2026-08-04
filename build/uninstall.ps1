[CmdletBinding()]
param(
    [switch]$KeepSettings,
    [switch]$Quiet,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$stateRoots = @(
    (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Snapture'),
    (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Snapture')
)
$registrationKeys = @(
    'HKCU:\Software\Classes\snapture',
    'HKCU:\Software\Classes\SystemFileAssociations\image\shell\Snapture',
    'HKCU:\Software\Classes\AppUserModelId\SysAdminDoc.Snapture'
)
$registrationProperties = @(
    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'; Name = 'Snapture' }
)

function Remove-SnaptureRegistrations {
    foreach ($key in $registrationKeys) {
        if (Test-Path -LiteralPath $key) {
            if ($WhatIf) {
                Write-Host "Would remove registry registration: $key"
            }
            else {
                Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    foreach ($property in $registrationProperties) {
        if ($WhatIf) {
            Write-Host "Would remove registry value: $($property.Path)::$($property.Name)"
        }
        else {
            Remove-ItemProperty -LiteralPath $property.Path -Name $property.Name -Force -ErrorAction SilentlyContinue
        }
    }

    try {
        if ($WhatIf) {
            Write-Host 'Would remove the Snapture LAN Share firewall rule.'
            return
        }
        Get-NetFirewallRule -DisplayName 'Snapture LAN Share' -ErrorAction SilentlyContinue |
            Remove-NetFirewallRule -ErrorAction SilentlyContinue
    }
    catch { }
}

function Remove-SnaptureState {
    if ($KeepSettings) { return }
    foreach ($root in $stateRoots) {
        if (Test-Path -LiteralPath $root) {
            if ($WhatIf) {
                Write-Host "Would remove user state: $root"
            }
            else {
                [System.IO.Directory]::Delete($root, $true)
            }
        }
    }
}

if (-not $Quiet -and -not $KeepSettings) {
    try {
        Add-Type -AssemblyName PresentationFramework
        $window = [System.Windows.Window]::new()
        $window.Title = 'Uninstall Snapture'
        $window.Width = 430
        $window.Height = 210
        $window.WindowStartupLocation = 'CenterScreen'
        $panel = [System.Windows.Controls.StackPanel]::new()
        $panel.Margin = [System.Windows.Thickness]::new(22)
        $label = [System.Windows.Controls.TextBlock]::new()
        $label.Text = 'Remove Snapture and its local data?'
        $label.FontSize = 16
        $label.Margin = [System.Windows.Thickness]::new(0, 0, 0, 12)
        $panel.Children.Add($label) | Out-Null
        $keep = [System.Windows.Controls.CheckBox]::new()
        $keep.Content = 'Keep my settings, history, and plugins'
        $keep.Margin = [System.Windows.Thickness]::new(0, 0, 0, 18)
        $panel.Children.Add($keep) | Out-Null
        $buttons = [System.Windows.Controls.StackPanel]::new()
        $buttons.Orientation = 'Horizontal'
        $buttons.HorizontalAlignment = 'Right'
        $remove = [System.Windows.Controls.Button]::new()
        $remove.Content = 'Remove'
        $remove.Padding = [System.Windows.Thickness]::new(18, 7, 18, 7)
        $remove.Margin = [System.Windows.Thickness]::new(0, 0, 8, 0)
        $cancel = [System.Windows.Controls.Button]::new()
        $cancel.Content = 'Cancel'
        $cancel.Padding = [System.Windows.Thickness]::new(18, 7, 18, 7)
        $cancel.Add_Click({ $window.DialogResult = $false })
        $remove.Add_Click({
            $script:KeepSettings = $keep.IsChecked -eq $true
            $window.DialogResult = $true
        })
        $buttons.Children.Add($remove) | Out-Null
        $buttons.Children.Add($cancel) | Out-Null
        $panel.Children.Add($buttons) | Out-Null
        $window.Content = $panel
        if ($window.ShowDialog() -ne $true) { exit 0 }
    }
    catch {
        Write-Warning "Could not show the cleanup window; rerun with -KeepSettings or -Quiet. $($_.Exception.Message)"
        exit 2
    }
}

if ($WhatIf) {
    $dryRunMessage = if ($KeepSettings) {
        'Dry run: registrations only; settings kept.'
    }
    else {
        'Dry run: registrations and user state would be removed.'
    }
    Write-Host $dryRunMessage
}

Remove-SnaptureRegistrations
Remove-SnaptureState
$resultMessage = if ($WhatIf) {
    'Dry run complete; no files or registrations were changed.'
}
elseif ($KeepSettings) {
    'Snapture registrations removed; user data kept.'
}
else {
    'Snapture registrations and user data removed.'
}
Write-Host $resultMessage
