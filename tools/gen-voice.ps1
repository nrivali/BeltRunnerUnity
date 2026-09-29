# Vega, the ship's onboard assistant: records the tutorial lines into Assets/Resources/Sfx/tut_<id>.mp3 with ElevenLabs.
# The lines must match Tutorial.cs word for word (the card shows the same text). The API key is read from the sibling
# BeltRunner repo's git-ignored elevenlabs.key and never written anywhere. Run from the repo root:
#   powershell -ExecutionPolicy Bypass -File tools/gen-voice.ps1            (skips lines whose mp3 exists)
#   powershell -ExecutionPolicy Bypass -File tools/gen-voice.ps1 -Force     (re-records everything)
param([string[]]$Only=@(), [switch]$Force)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$key=(Get-Content (Join-Path (Split-Path -Parent $root) 'BeltRunner\elevenlabs.key') -Raw).Trim()
$out=Join-Path $root 'Assets\Resources\Sfx'
$VOICE='WtA85syCrJwasGeHGH2p'   # the voice of Vega (the user's pick, 2026-09-15): energetic, approachable, friendly
$lines=[ordered]@{
  launch   = "Vega here. Press Enter on the pad. Approach control taxis you out; the ship is yours when it lets go."
  stick    = "The mouse aims. W and S work the throttle, A and D roll, Shift lights the afterburner, and Space cuts the engine so you drift and swing the nose. Open up and give me a turn."
  cockpit  = "Press P for the cockpit. The three screens carry the ship, the flight and the target; hold the right mouse button to look round. P again for the chase camera."
  band     = "The band along the bottom: hull, shield, fuel and hold on the left; then speed and the way home; the target you are looking at; the weapon in hand. The scope at the right shows what is round you."
  pulse    = "Press R to pulse the radar. Ore rocks light up with their range for a while; coloured veins mean ore, grey rock is barren, so skip it."
  cut      = "Fly at a copper rock, orange veins, and hold the left mouse button with the crosshair on it. The beam cuts where you aim it; hold it on the glowing seam, the weak spot, and the rock cracks. Its ore comes to you."
  guns     = "2 is the autocannon, 3 the seeker rockets, 1 the mining laser; the wheel cycles them. The cannon overheats if you hold it, so let it cool. Try one."
  raiders  = "Raiders come for you in the belt: red diamonds on the HUD and the scope. Put the crosshair on one and fire; Z locks it and steers your nose onto its lead. Your shield soaks hits and is back five seconds after the last."
  flares   = "Seeker rockets home on you. Press B to drop a flare and they chase it instead; a hard turn at the last moment throws one too. Drop one now."
  return   = "Follow the CARGO SHIP readout. Within 560 press H and approach control brings you in."
  stow     = "Press E to move your ore into the cargo ship storage. The pad refuels you and mends the hull while you sit on it."
  services = "On the pad, F opens the services: the workshop turns ore into parts, flares and rockets, and the refits sharpen the ship. Nothing sells out here; press N and warp to the Hub for that."
  front    = "The Front is a battlefield: two battleships six kilometres apart. The fight starts in the middle; kills push the meter, and the side that fills it attacks the other ship until it is pushed back. Five pushes, or the core, take a ship."
  depart   = "Press Depart, or Enter on the pad, to launch."
  done     = "That is the loop: fill the hold, stow it, sell at the Hub, upgrade. Vega out."
}
$chars=0
foreach ($id in $lines.Keys){
  if ($Only.Count -gt 0 -and $Only -notcontains $id) { continue }
  $file=Join-Path $out ("tut_$id.mp3")
  if ((Test-Path $file) -and -not $Force) { "skip  tut_$id (exists)"; continue }
  # v3 ignores speed and break tags: pacing comes from the sentence ends. The audio tag sets the delivery (energetic,
  # approachable, friendly) and is not spoken; stability 0.35 leans "creative" so the tag actually shows in the read
  $text='[cheerful] [upbeat] '+($lines[$id] -replace '\. ', '... ')
  $body=@{text=$text; model_id='eleven_v3'; voice_settings=@{stability=0.35; similarity_boost=0.8}} | ConvertTo-Json -Depth 4
  try {
    Invoke-WebRequest -Uri ("https://api.elevenlabs.io/v1/text-to-speech/$VOICE"+'?output_format=mp3_44100_96') -Method Post -Headers @{'xi-api-key'=$key; 'Content-Type'='application/json'; 'Accept'='audio/mpeg'} -Body ([Text.Encoding]::UTF8.GetBytes($body)) -OutFile $file | Out-Null
    $chars+=$text.Length; "made  tut_$id  $((Get-Item $file).Length) bytes"
  } catch { $d=''; try { $rd=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream()); $d=' '+$rd.ReadToEnd() } catch {}; "FAIL  tut_$id : $($_.Exception.Message)$d" }
}
# Vega's warnings and calls outside the tutorial: vega_<id>.mp3, read calm and clear
$vega=[ordered]@{
  fuel50 = "Fuel, fifty percent."
  fuel25 = "Fuel low, twenty-five percent."
  fuel10 = "Fuel critical, ten percent."
}
foreach ($id in $vega.Keys){
  if ($Only.Count -gt 0 -and $Only -notcontains $id) { continue }
  $file=Join-Path $out ("vega_$id.mp3")
  if ((Test-Path $file) -and -not $Force) { "skip  vega_$id (exists)"; continue }
  $text=($vega[$id] -replace '\. ', '... ')   # a straight read, no audio tag (an earlier version mangled every word with a bad regex)
  $body=@{text=$text; model_id='eleven_v3'; voice_settings=@{stability=0.5; similarity_boost=0.8}} | ConvertTo-Json -Depth 4
  try {
    Invoke-WebRequest -Uri ("https://api.elevenlabs.io/v1/text-to-speech/$VOICE"+'?output_format=mp3_44100_96') -Method Post -Headers @{'xi-api-key'=$key; 'Content-Type'='application/json'; 'Accept'='audio/mpeg'} -Body ([Text.Encoding]::UTF8.GetBytes($body)) -OutFile $file | Out-Null
    $chars+=$text.Length; "made  vega_$id  $((Get-Item $file).Length) bytes"
  } catch { $d=''; try { $rd=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream()); $d=' '+$rd.ReadToEnd() } catch {}; "FAIL  vega_$id : $($_.Exception.Message)$d" }
}
"characters spent: $chars"
