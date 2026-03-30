$text = [IO.File]::ReadAllText("C:\Users\Mirko\.gemini\antigravity\brain\f422e5c0-76c4-4bb3-b4dd-17710dffa354\.system_generated\logs\overview.txt")
$match = [regex]::Match($text, '(?s)(<Border x:Name="SurgeryTabContainer".*?<!-- ═══ FULLSCREEN PHOTOGRAMMETRY OVERLAY ═══ -->)')
if ($match.Success) {
    [IO.File]::WriteAllText("C:\Users\Mirko\Documents\Orthoplanner\src\OrthoPlanner.App\recovered_surgery.txt", $match.Groups[1].Value)
    Write-Host "Success!"
} else {
    Write-Host "Not found"
}
