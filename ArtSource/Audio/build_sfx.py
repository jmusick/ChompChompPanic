"""Synthesize the sound effects for Chomp Chomp Panic! procedurally.

Run: python build_sfx.py
Writes 16-bit mono 44.1 kHz WAVs to Assets/Audio (assigned by Chomp Chomp Panic > Import Audio,
which maps sfx_<name>.wav to the <name>Sound field on the GameManager / TitleScreen):
  sfx_chomp.wav        wet bite when the kaiju eats a person or soldier
  sfx_crunch.wav       metal crunch when it eats a vehicle or a rival
  sfx_gunshot.wav      rifle / machine-gun shot
  sfx_launch.wav       rocket, tank shell or missile leaving the barrel
  sfx_explosion.wav    shell / rocket / missile blast
  sfx_hit.wav          bullet thudding off the kaiju's hide
  sfx_smash_<n>.wav    building crumbling (4 variants)
  sfx_stomp.wav        kaiju footstep
  sfx_grow.wav         rising chiptune arpeggio: big enough to eat the next tier
  sfx_win.wav          fanfare when the session timer runs out
  sfx_lose.wav         falling jingle when the kaiju is chomped or taken down
  sfx_menu_move.wav    title menu selection blip
  sfx_menu_select.wav  title menu confirm
Standard library only. Output is deterministic (every sound has its own seed).
"""
from pathlib import Path
import math
import random
import struct
import wave

HERE = Path(__file__).resolve().parent
GAME = HERE.parents[1] / "Assets" / "Audio"
RATE = 44100
TAU = math.tau


# ------------------------------------------------------------------ building blocks
# A sound is a plain list of floats in -1..1. Functions of time take t in seconds.

def silence(seconds):
    return [0.0] * int(seconds * RATE)


def mix(*layers):
    """Sum layers of possibly different lengths; each layer is (offset_seconds, samples, gain)."""
    end = max(int(off * RATE) + len(s) for off, s, _ in layers)
    out = [0.0] * end
    for off, s, gain in layers:
        start = int(off * RATE)
        for i, v in enumerate(s):
            out[start + i] += v * gain
    return out


def env(n, attack, decay, hold=0.0):
    """Linear attack, optional hold, then exponential decay with time constant `decay` (seconds)."""
    a = max(1, int(attack * RATE))
    h = int(hold * RATE)
    out = []
    for i in range(n):
        if i < a:
            out.append(i / a)
        elif i < a + h:
            out.append(1.0)
        else:
            out.append(math.exp(-(i - a - h) / (decay * RATE)))
    return out


def apply(samples, envelope):
    return [s * e for s, e in zip(samples, envelope)]


def white(n, rng):
    return [rng.uniform(-1.0, 1.0) for _ in range(n)]


def brown(n, rng):
    """Integrated white noise with a slow leak: deep rumble."""
    out, v = [], 0.0
    for _ in range(n):
        v = v * 0.995 + rng.uniform(-1.0, 1.0) * 0.1
        out.append(v)
    return normalize(out, 1.0)


def lowpass(samples, cutoff):
    """One-pole low-pass; `cutoff` is Hz or a function of t for a sweep."""
    out, y = [], 0.0
    for i, x in enumerate(samples):
        fc = cutoff(i / RATE) if callable(cutoff) else cutoff
        k = 1.0 - math.exp(-TAU * fc / RATE)
        y += k * (x - y)
        out.append(y)
    return out


def resonant(samples, freq, q):
    """Resonant band-pass (RBJ biquad, 0 dB peak); `freq` is Hz or a function of t. Gives noise a vowel-like, wet tone."""
    out = []
    x1 = x2 = y1 = y2 = 0.0
    for i, x in enumerate(samples):
        f = freq(i / RATE) if callable(freq) else freq
        w = TAU * f / RATE
        alpha = math.sin(w) / (2 * q)
        a0 = 1 + alpha
        y = (alpha * x - alpha * x2 + 2 * math.cos(w) * y1 - (1 - alpha) * y2) / a0
        x2, x1, y2, y1 = x1, x, y1, y
        out.append(y)
    return out


def clicks(seconds, rng, count, spread):
    """Sharp single-sample snaps of random sign and size, bunched at the start: the crack of breaking bone."""
    out = [0.0] * int(seconds * RATE)
    for _ in range(count):
        i = int(min(seconds * 0.95, rng.expovariate(1.0 / spread)) * RATE)
        out[i] += rng.choice((-1, 1)) * rng.uniform(0.2, 1.0)
    return out


def highpass(samples, cutoff):
    low = lowpass(samples, cutoff)
    return [x - l for x, l in zip(samples, low)]


def bandpass(samples, low, high):
    return lowpass(highpass(samples, low), high)


def sweep(t0, t1, seconds, curve=1.0):
    """A function of t going from t0 to t1 over `seconds` (exponential when both are positive)."""
    def f(t):
        x = min(1.0, t / seconds) ** curve
        return t0 * (t1 / t0) ** x
    return f


def osc(seconds, freq, shape="sine", duty=0.5):
    """Oscillator with a constant or swept frequency (Hz or function of t)."""
    out, phase = [], 0.0
    for i in range(int(seconds * RATE)):
        f = freq(i / RATE) if callable(freq) else freq
        phase = (phase + f / RATE) % 1.0
        if shape == "sine":
            out.append(math.sin(TAU * phase))
        elif shape == "square":
            out.append(1.0 if phase < duty else -1.0)
        elif shape == "triangle":
            out.append(4.0 * abs(phase - 0.5) - 1.0)
        elif shape == "saw":
            out.append(2.0 * phase - 1.0)
    return out


def note(name):
    """Frequency of a note like 'C5' or 'Eb4'."""
    steps = {"C": -9, "D": -7, "E": -5, "F": -4, "G": -2, "A": 0, "B": 2}
    letter, rest = name[0], name[1:]
    shift = 0
    while rest and rest[0] in "#b":
        shift += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    semis = steps[letter] + shift + (int(rest) - 4) * 12
    return 440.0 * 2 ** (semis / 12)


def grains(seconds, rng, count, length, cutoff, density_decay):
    """Scattered short noise bursts (crackle, debris), denser at the start."""
    out = [0.0] * int(seconds * RATE)
    for _ in range(count):
        t = min(seconds - length, rng.expovariate(1.0 / density_decay))
        g_len = rng.uniform(0.4, 1.0) * length
        g = lowpass(white(int(g_len * RATE), rng), rng.uniform(*cutoff))
        g = apply(g, env(len(g), 0.001, g_len * 0.3))
        gain = rng.uniform(0.3, 1.0) * math.exp(-t / (seconds * 0.6))
        start = int(t * RATE)
        for i, v in enumerate(g):
            if start + i < len(out):
                out[start + i] += v * gain
    return out


def normalize(samples, peak=0.89):
    m = max(abs(s) for s in samples) or 1.0
    return [s * peak / m for s in samples]


def finish(samples, fade=0.01, peak=0.89):
    """Remove DC, normalize to about -1 dBFS and fade the tail so nothing clicks."""
    samples = highpass(samples, 20.0)
    samples = normalize(samples, peak)
    n = min(len(samples), int(fade * RATE))
    for i in range(n):
        samples[-1 - i] *= i / n
    return samples


def write(name, samples):
    GAME.mkdir(parents=True, exist_ok=True)
    path = GAME / f"{name}.wav"
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s)) * 32767)) for s in samples))
    print(f"wrote {path.relative_to(HERE.parents[1])} ({len(samples) / RATE:.2f} s)")


# ------------------------------------------------------------------ sounds

def chew(rng, strength, pitch):
    """One bite inside a closed mouth: bones cracking, flesh squishing and the jaw thudding shut."""
    seconds = 0.2
    n = int(seconds * RATE)
    # Bone: dense snaps rung through a couple of hollow resonances, muffled by the mouth.
    snaps = clicks(seconds, rng, int(70 * strength), 0.035)
    bone = [a + b for a, b in zip(resonant(snaps, 1800 * pitch, 3), resonant(snaps, 3100 * pitch, 4))]
    bone = lowpass(bone, 5000)
    # Flesh: noise through a sliding "ow" vowel, the wet part of the chew.
    squish = resonant(white(n, rng), sweep(1300 * pitch, 380 * pitch, 0.1), 5)
    squish = apply(squish, env(n, 0.008, 0.045))
    # Jaw: a short, low knock.
    jaw = apply(osc(seconds, sweep(160 * pitch, 70 * pitch, 0.06)), env(n, 0.001, 0.03))
    return mix((0, normalize(bone, 1.0), 0.9 * strength), (0, normalize(squish, 1.0), 0.55), (0, jaw, 0.6 * strength))


def chomp(rng):
    # Teeth snap shut on the first bite, then the kaiju munches twice more, each a little softer.
    m = int(0.03 * RATE)
    clack = apply(highpass(white(m, rng), 2500), env(m, 0.0003, 0.004))
    return mix((0, clack, 0.45),
               (0.0, chew(rng, 1.0, 1.0), 1.0),
               (0.17, chew(rng, 0.75, 0.92), 0.8),
               (0.33, chew(rng, 0.5, 1.05), 0.6))


def crunch(rng):
    seconds = 0.55
    n = int(seconds * RATE)
    thump = apply(osc(seconds, sweep(110, 40, 0.25)), env(n, 0.002, 0.1))
    body = apply(lowpass(white(n, rng), sweep(3000, 300, 0.3)), env(n, 0.002, 0.09))
    crackle = grains(seconds, rng, 70, 0.012, (1500, 6000), 0.12)
    # Bent sheet metal: a few inharmonic partials roughened by noise.
    metal = [0.0] * n
    for f, d in ((523, 0.12), (1347, 0.08), (2211, 0.05), (3170, 0.04)):
        part = apply(osc(seconds, f * rng.uniform(0.97, 1.03)), env(n, 0.002, d))
        metal = [a + b for a, b in zip(metal, part)]
    grit = lowpass(white(n, rng), 2000)
    metal = [m * (0.5 + 0.5 * g) for m, g in zip(metal, grit)]
    return mix((0, thump, 0.9), (0, body, 0.8), (0, crackle, 0.6), (0.01, metal, 0.35))


def gunshot(rng):
    seconds = 0.2
    n = int(seconds * RATE)
    crack = apply(highpass(white(n, rng), 1500), env(n, 0.0005, 0.008))
    body = apply(lowpass(white(n, rng), sweep(5000, 600, 0.1)), env(n, 0.001, 0.035))
    thump = apply(osc(seconds, sweep(180, 60, 0.08)), env(n, 0.001, 0.03))
    return mix((0, crack, 0.8), (0, body, 1.0), (0, thump, 0.6))


def launch(rng):
    seconds = 0.7
    n = int(seconds * RATE)
    boom = apply(osc(seconds, sweep(120, 38, 0.3)), env(n, 0.002, 0.13))
    blast = apply(lowpass(white(n, rng), sweep(2500, 180, 0.35)), env(n, 0.002, 0.12))
    crack = apply(highpass(white(n, rng), 1200), env(n, 0.0005, 0.012))
    # The whoosh of the round leaving: rising band of noise.
    hiss = apply(bandpass(white(n, rng), 600, 3500), env(n, 0.04, 0.12))
    return mix((0, boom, 1.0), (0, blast, 0.9), (0, crack, 0.5), (0.02, hiss, 0.25))


def explosion(rng):
    seconds = 1.6
    n = int(seconds * RATE)
    rumble = apply(lowpass(brown(n, rng), sweep(2200, 120, 1.0, 0.6)), env(n, 0.004, 0.38))
    blast = apply(lowpass(white(n, rng), sweep(6000, 400, 0.3)), env(n, 0.001, 0.06))
    sub = apply(osc(seconds, sweep(70, 28, 0.6)), env(n, 0.003, 0.25))
    debris = grains(seconds, rng, 90, 0.015, (800, 5000), 0.35)
    return mix((0, rumble, 1.0), (0, blast, 0.8), (0, sub, 0.8), (0.05, debris, 0.3))


def hit(rng):
    seconds = 0.12
    n = int(seconds * RATE)
    thud = apply(lowpass(white(n, rng), 900), env(n, 0.001, 0.018))
    knock = apply(osc(seconds, sweep(240, 120, 0.05)), env(n, 0.001, 0.025))
    tick = apply(highpass(white(n, rng), 3000), env(n, 0.0003, 0.003))
    return mix((0, thud, 1.0), (0, knock, 0.8), (0, tick, 0.3))


def smash(rng, seconds=1.1, thud=95, rubble_count=140, rubble_tone=(400, 2500), glass_gain=0.25, rumble_decay=0.3):
    """A building coming down. The variants below change the mix so a city-wide rampage doesn't loop one clip."""
    n = int(seconds * RATE)
    impact = apply(osc(seconds, sweep(thud, thud * 0.37, 0.3)), env(n, 0.002, 0.12))
    crash = apply(lowpass(white(n, rng), sweep(4500, 400, 0.4)), env(n, 0.002, 0.1))
    rumble = apply(lowpass(brown(n, rng), 500), env(n, 0.01, rumble_decay))
    # Falling concrete and glass.
    rubble = grains(seconds, rng, rubble_count, 0.03, rubble_tone, seconds * 0.23)
    glass = grains(seconds, rng, 40, 0.008, (5000, 9000), 0.15)
    glass = highpass(glass, 3000)
    return mix((0, impact, 1.0), (0, crash, 0.7), (0, rumble, 0.6), (0.02, rubble, 0.7), (0.03, glass, glass_gain))


def smash_glass(rng):
    """An office block: lighter thud, lots of shattering windows."""
    return smash(rng, seconds=0.9, thud=120, rubble_count=90, glass_gain=0.6, rumble_decay=0.2)


def smash_heavy(rng):
    """A concrete block: deep thud, a long slide of rubble, no glass to speak of."""
    return smash(rng, seconds=1.4, thud=75, rubble_count=200, rubble_tone=(250, 1500), glass_gain=0.08, rumble_decay=0.45)


def smash_short(rng):
    """A small shop folding up: quick and crunchy."""
    return smash(rng, seconds=0.7, thud=110, rubble_count=110, rubble_tone=(600, 3500), glass_gain=0.3, rumble_decay=0.15)


def stomp(rng):
    seconds = 0.4
    n = int(seconds * RATE)
    # Two partials so it still reads as a thud on small speakers.
    low = apply(osc(seconds, sweep(80, 32, 0.2)), env(n, 0.003, 0.09))
    mid = apply(osc(seconds, sweep(170, 70, 0.15)), env(n, 0.002, 0.04))
    dirt = apply(lowpass(white(n, rng), 600), env(n, 0.001, 0.03))
    return mix((0, low, 1.0), (0, mid, 0.5), (0, dirt, 0.5))


def chiptune(notes, shape="square", duty=0.25, decay=0.12, vibrato=0.0):
    """A sequence of (note, start, length) played on a simple chip voice."""
    layers = []
    for name, start, length in notes:
        f = note(name)
        freq = (lambda t, f=f: f * (1 + vibrato * math.sin(TAU * 6 * t))) if vibrato else f
        tone = osc(length, freq, shape, duty)
        layers.append((start, apply(tone, env(len(tone), 0.004, decay, hold=length * 0.3)), 1.0))
    return lowpass(mix(*layers), 7000)


def grow(rng):
    arp = chiptune([("C5", 0.0, 0.1), ("E5", 0.08, 0.1), ("G5", 0.16, 0.1), ("C6", 0.24, 0.45)],
                   decay=0.1, vibrato=0.01)
    sub = chiptune([("C4", 0.24, 0.45)], shape="triangle", decay=0.2)
    return mix((0, arp, 1.0), (0, sub, 0.6))


def win(rng):
    lead = chiptune([("G4", 0.0, 0.12), ("C5", 0.12, 0.12), ("E5", 0.24, 0.12), ("G5", 0.36, 0.2),
                     ("E5", 0.58, 0.1), ("G5", 0.68, 0.7)], decay=0.15, vibrato=0.008)
    bass = chiptune([("C3", 0.0, 0.36), ("G3", 0.36, 0.32), ("C4", 0.68, 0.7)], shape="triangle", decay=0.3)
    return mix((0, lead, 1.0), (0, bass, 0.7))


def lose(rng):
    lead = chiptune([("G4", 0.0, 0.18), ("Eb4", 0.2, 0.18), ("C4", 0.4, 0.18)], duty=0.5, decay=0.15)
    # Final note sags in pitch like a deflating monster.
    tail = osc(0.9, sweep(note("B3"), note("B3") / 2.5, 0.9))
    tail = lowpass([1.0 if s > 0 else -1.0 for s in tail], 3000)
    tail = apply(tail, env(len(tail), 0.004, 0.35, hold=0.15))
    bass = chiptune([("C3", 0.0, 0.6), ("G2", 0.6, 0.9)], shape="triangle", decay=0.35)
    return mix((0, lead, 1.0), (0.6, tail, 0.8), (0, bass, 0.6))


def menu_move(rng):
    return chiptune([("A5", 0.0, 0.05)], decay=0.02)


def menu_select(rng):
    return chiptune([("E5", 0.0, 0.07), ("B5", 0.07, 0.18)], decay=0.06)


# Each sound's seed is its position in this list, so add new sounds at the end to keep the others unchanged.
# Variants of one sound are named sfx_<name>_<n> and fill the <name>Sounds array.
SOUNDS = {
    "sfx_chomp": chomp,
    "sfx_crunch": crunch,
    "sfx_gunshot": gunshot,
    "sfx_launch": launch,
    "sfx_explosion": explosion,
    "sfx_hit": hit,
    "sfx_smash_1": smash,
    "sfx_stomp": stomp,
    "sfx_grow": grow,
    "sfx_win": win,
    "sfx_lose": lose,
    "sfx_menu_move": menu_move,
    "sfx_menu_select": menu_select,
    "sfx_smash_2": smash_glass,
    "sfx_smash_3": smash_heavy,
    "sfx_smash_4": smash_short,
}


def main():
    for seed, (name, build) in enumerate(SOUNDS.items()):
        write(name, finish(build(random.Random(1000 + seed))))


if __name__ == "__main__":
    main()
