# Sound effects for the Unity port, made with the ElevenLabs sound-generation endpoint into Assets/Resources/Sfx/<name>.mp3.
# The API key is read from the sibling BeltRunner repo's git-ignored elevenlabs.key and never written anywhere. Run from
# the repo root:
#   powershell -ExecutionPolicy Bypass -File tools/gen-sfx.ps1            (skips clips whose mp3 exists)
#   powershell -ExecutionPolicy Bypass -File tools/gen-sfx.ps1 -Force     (remakes everything)
#   ... -Only shield_down,shield_up                                        (just those)
param([string[]]$Only=@(), [switch]$Force)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$key=(Get-Content (Join-Path (Split-Path -Parent $root) 'BeltRunner\elevenlabs.key') -Raw).Trim()
$out=Join-Path $root 'Assets\Resources\Sfx'
$clips=@(
  # the shield: the moment it is stripped, the time it is down, the moment it comes back
  @{name='shield_down'; dur=1.4; text='sci-fi spaceship energy shield collapsing, a sharp electric crack then a falling power-down whine with a fizzing tail, no music'},
  # shield_out.mp3 (DepletedShields.mp3) and shield_charge.mp3 (ShieldRecharge.mp3) are the user's own clips, 2026-09-14, not generated here
  @{name='shield_up';   dur=1.6; text='sci-fi spaceship energy shield recharging and snapping back on, a rising electric charge swell ending in a clean bright lock-in chime, no music'}
)
foreach ($c in $clips) {
  if ($Only.Count -gt 0 -and $Only -notcontains $c.name) { continue }
  $file=Join-Path $out ($c.name+'.mp3')
  if ((Test-Path $file) -and -not $Force) { "skip  $($c.name) (exists)"; continue }
  $b=@{text=$c.text; duration_seconds=$c.dur; prompt_influence=0.35}; if ($c.loop) { $b.loop=$true }
  $body=$b | ConvertTo-Json
  try {
    Invoke-WebRequest -Uri 'https://api.elevenlabs.io/v1/sound-generation' -Method Post -Headers @{'xi-api-key'=$key; 'Content-Type'='application/json'} -Body $body -OutFile $file | Out-Null
    "made  $($c.name)  $((Get-Item $file).Length) bytes"
  } catch { $d=''; try { $rd=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream()); $d=' '+$rd.ReadToEnd() } catch {}; "FAIL  $($c.name) : $($_.Exception.Message)$d" }
}
