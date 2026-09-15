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
  # shield_out.mp3 (DepletedShields.mp3), shield_charge.mp3 (ShieldRecharge.mp3) and blaster.mp3 (Blaster.mp3) are the user's own clips, 2026-09-14, not generated here
  # the hit marker: a punchy tick on a hit, a sting on the kill
  @{name='hit_marker';  dur=0.5; text='loud punchy arcade hit marker, a sharp bright metallic click with a hard short thump, instant attack, very short, satisfying, no music'},
  @{name='kill_marker'; dur=0.9; text='arcade kill confirmation, a deep punchy bass thump with a crisp high snap layered on top, one single hit, very short, satisfying, no melody, no music'},
  # a raider opening the throttle: a whoosh and roar that passes
  @{name='raider_boost'; dur=1.8; text='spaceship afterburner igniting and roaring past, a sharp whoosh into a deep rumbling roar that fades, no music'},
  # a raider's engine close by: the bed under a fight
  @{name='raider_engine'; dur=4.0; loop=$true; text='small spaceship engine under steady thrust, a soft low hum with a light turbine whine, seamless loop, quiet, no music'},
  # a raider's afterburner, sustained for as long as it boosts
  @{name='raider_boost_loop'; dur=4.0; loop=$true; text='spaceship afterburner roaring at full power, deep rumbling roar with an airy exhaust rush, seamless loop, no music'},
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
