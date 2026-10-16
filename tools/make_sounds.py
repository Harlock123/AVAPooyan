#!/usr/bin/env python3
"""Synthesises AVAPooyan's sound effects and tunes into ../src/AVAPooyan/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPooyan", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def drums(bpm, bars):
    """A marching snare: a roll on the first beat of each bar, single taps on the rest, and a bass drum under it."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            if b == 0:
                hit = cat(*[tone(beat / 4, 1800, 900, "noise", attack=0.001, release=1.5, gain=0.5) for _ in range(4)])
            else:
                hit = cat(tone(beat / 2, 2000, 700, "noise", attack=0.001, release=2.5, gain=0.6), silence(beat / 2))
            kick = cat(tone(beat / 2, 110, 45, "sine", attack=0.002, release=1.5, gain=0.9), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The bow: a twang. A balloon: a sharp pop. A wolf falling: a long whistle down.
    write("Shoot.wav", mix(tone(0.18, 220, 180, "tri", attack=0.002, vibrato=0.04, vib_rate=30, release=1.2), tone(0.05, 1800, 900, "noise", release=1.5, gain=0.4)), 0.3)
    write("Pop.wav", mix(tone(0.06, 3000, 800, "noise", attack=0.001, release=1.8), tone(0.05, 600, 200, "square", release=1.5, gain=0.4)), 0.4)
    write("Fall.wav", tone(0.7, 1400, 300, "sine", vibrato=0.01, release=0.4), 0.2)
    write("Land.wav", mix(tone(0.12, 200, 70, "sine", release=1), tone(0.08, 600, 200, "noise", release=1.4, gain=0.4)), 0.3)
    write("Climb.wav", cat(*[tone(0.06, 500 + i * 60, kind="square", release=0.8, gain=0.4) for i in range(4)]), 0.18)
    write("Throw.wav", tone(0.2, 400, 900, "noise", attack=0.02, release=0.8), 0.16)
    write("RockHit.wav", mix(tone(0.1, 900, 400, "square", release=1.2, gain=0.5), tone(0.08, 2000, 700, "noise", release=1.5, gain=0.5)), 0.3)
    write("Meat.wav", tone(0.3, 300, 700, "saw", vibrato=0.05, vib_rate=12, release=0.6, gain=0.5), 0.26)
    write("MeatReady.wav", cat(tone(0.08, hz("C6"), kind="pulse", release=0.3), tone(0.08, hz("E6"), kind="pulse", release=0.3), tone(0.14, hz("G6"), kind="pulse", release=0.5)), 0.28)
    # The wolf's bite: a growl and a snap. The boulder: a rumbling roll.
    write("Bite.wav", cat(tone(0.35, 110, 90, "saw", vibrato=0.15, vib_rate=25, release=0.4), tone(0.06, 1600, 600, "noise", release=1.5)), 0.42)
    write("Boulder.wav", mix(tone(1.6, 90, 50, "noise", attack=0.1, release=0.3), tone(1.6, 60, 40, "sine", attack=0.1, release=0.3, gain=0.8)), 0.44)
    write("Hit.wav", cat(tone(0.08, 600, 300, "square", release=0.6), *[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4"]]), 0.4)
    write("LevelStart.wav", cat(*[tone(d, hz(n), kind="pulse", release=0.25) for n, d in [("C5", 0.1), ("F5", 0.1), ("A5", 0.3)]]), 0.3)
    write("LevelClear.wav", tune(150, [("F5", 0.5), ("A5", 0.5), ("C6", 0.5), ("F6", 1), ("E6", 0.5), ("F6", 2)], [("F2", 1), ("C3", 1), ("F2", 1), ("C3", 0.5), ("F2", 1.5)], kind="pulse"), 0.42)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["F5", "C6", "F6", "C6", "F6"]]), 0.36)

    # In the valley: a cheerful oom-pah (an original composition), a whole number of beats long so it loops.
    lead = [("F5", 0.5), ("A5", 0.5), ("C6", 1), ("A5", 0.5), ("F5", 0.5), ("G5", 1),
            ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("Bb5", 0.5), ("G5", 0.5), ("A5", 1),
            ("F5", 0.5), ("A5", 0.5), ("D6", 1), ("C6", 0.5), ("A5", 0.5), ("Bb5", 1),
            ("G5", 0.5), ("E5", 0.5), ("C5", 0.5), ("E5", 0.5), ("F5", 2)]
    oom = lambda root, fifth: [(root, 0.5), (fifth, 0.5)]
    bass = oom("F2", "C3") * 2 + oom("C2", "G2") * 2 + oom("C2", "G2") * 2 + oom("F2", "C3") * 2 \
        + oom("D2", "A2") * 2 + oom("Bb2", "F2") * 2 + oom("C2", "G2") * 2 + [("F2", 1), ("C2", 1)]
    melody = tune(140, lead, bass, kind="pulse")
    write("Music.wav", melody, 0.28)

    # The title: the tune, a second strain, and a march.
    title_lead = lead + [("C6", 1), ("A5", 0.5), ("F5", 0.5), ("D6", 1), ("C6", 1), ("Bb5", 0.5), ("A5", 0.5), ("G5", 0.5), ("A5", 0.5), ("Bb5", 2),
                         ("A5", 1), ("F5", 0.5), ("A5", 0.5), ("G5", 1), ("E5", 1), ("F5", 0.5), ("G5", 0.5), ("E5", 0.5), ("C5", 0.5), ("F5", 2)]
    title_bass = bass + oom("F2", "C3") * 2 + oom("Bb2", "F2") * 2 + oom("C2", "G2") * 2 + oom("Bb2", "F2") * 2 \
        + oom("F2", "C3") * 2 + oom("C2", "G2") * 2 + oom("C2", "G2") * 2 + [("F2", 1), ("C2", 1)]
    write("TitleTune.wav", cat(mix(tune(140, title_lead, title_bass, kind="pulse"), drums(140, 8)), silence(1.0)), 0.4)
    write("StartTune.wav", tune(150, [("C5", 0.5), ("F5", 0.5), ("A5", 0.5), ("C6", 1), ("A5", 0.5), ("F6", 1)], [("F2", 1), ("C3", 1), ("F2", 2)], kind="pulse"), 0.42)
    write("GameOver.wav", tune(70, [("A5", 1), ("G5", 1), ("F5", 1), ("E5", 1), ("F5", 4)], [("F2", 2), ("C3", 2), ("Bb2", 2), ("F2", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
