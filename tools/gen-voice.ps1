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
  launch  = "Vega here. Press Enter on the pad. Approach control taxis you out; the ship is yours when it lets go."
  steer   = "The mouse aims. Shift and Ctrl work the throttle, A and D roll, Q and E yaw, X cuts the throttle. Open up and give me a turn."
  hud     = "The band along the bottom: SHIP is hull, shield, fuel and hold. FLIGHT is speed and the way home. TARGET is what you are looking at. WEAPON is the one in hand."
  radar   = "Press R to pulse the radar. Coloured veins mean ore. Grey rock is barren, so skip it."
  lock    = "Put the mouse on a copper rock, orange veins, and press Z to lock it."
  mine    = "Get within laser reach, put the crosshair on it and hold the left mouse button. When the rock breaks, the ore comes to you."
  weapons = "2 is the autocannon, 3 the seeker rockets, 1 the mining laser. The wheel cycles them. Try one."
  combat  = "Raiders hold the rich pockets. Lock one with Z and fire; a rocket chases it on its own. Your shield soaks hits and recharges once they stop."
  return  = "Follow the CARGO SHIP readout. Within 560 press H and approach control brings you in."
  stow    = "Press E to move your ore into the cargo ship storage. The pad refuels you and mends the hull while you sit on it."
  hub     = "Nothing sells out here. Press N and warp to the Hub to sell and upgrade."
  depart  = "Press Depart, or Enter on the pad, to launch."
  done    = "That is the loop: fill the hold, stow it, sell at the Hub, upgrade. Vega out."
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
