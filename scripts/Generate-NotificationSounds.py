"""Generate original short PCM notification presets (no external audio assets)."""
import math,struct,wave
from pathlib import Path
DEST=Path(__file__).resolve().parents[1]/'src/StatusLookout.Windows/Assets/Sounds'
DEST.mkdir(parents=True,exist_ok=True)
RATE=44100
def write(name,notes):
    length=max(start+duration for start,duration,pitch in notes)+.12
    samples=[]
    for i in range(int(length*RATE)):
        t=i/RATE;value=0
        for start,duration,pitch in notes:
            elapsed=t-start
            if 0<=elapsed<duration:
                attack=min(1,elapsed/.014);release=min(1,(duration-elapsed)/.08)
                envelope=attack*release*math.exp(-3.4*elapsed/duration)
                value+=(math.sin(2*math.pi*pitch*elapsed)+.15*math.sin(4*math.pi*pitch*elapsed))*envelope*.25
        samples.append(struct.pack('<h',int(max(-.9,min(.9,value))*32767)))
    with wave.open(str(DEST/(name+'.wav')),'wb') as output:
        output.setparams((1,2,RATE,0,'NONE','not compressed'));output.writeframes(b''.join(samples))
write('chime',[(0,.55,659.25),(.14,.65,987.77)])
write('pulse',[(0,.24,523.25),(.3,.24,523.25)])
write('recovery',[(0,.35,523.25),(.15,.4,659.25),(.3,.55,783.99)])
write('soft',[(0,.8,440),(0,.8,660)])
print('Generated four original notification sounds.')
