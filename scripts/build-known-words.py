"""Builds src/Notaker/Assets/known-words.txt.gz from FrequencyWords 2018 full lists (CC BY-SA 4.0).

Usage: python scripts/build-known-words.py es_full.txt en_full.txt
Source: https://github.com/hermitdave/FrequencyWords/tree/master/content/2018/{es,en}/{es,en}_full.txt
Keeps words seen at least MIN_COUNT times, lowercased, accents removed, letters a-z only, two letters or more.
"""
import gzip, re, sys, unicodedata

MIN_COUNT = 10
OUT = 'src/Notaker/Assets/known-words.txt.gz'

words = set()
for path in sys.argv[1:]:
    with open(path, encoding='utf-8') as source:
        for line in source:
            parts = line.split()
            if len(parts) != 2 or int(parts[1]) < MIN_COUNT:
                continue
            word = unicodedata.normalize('NFC', parts[0].lower())
            if not re.fullmatch(r'[^\W\d_]+', word):
                continue
            plain = ''.join(c for c in unicodedata.normalize('NFD', word) if unicodedata.category(c) != 'Mn')
            if len(plain) >= 2 and re.fullmatch(r'[a-z]+', plain):
                words.add(plain)
data = ('\n'.join(sorted(words)) + '\n').encode('utf-8')
with open(OUT, 'wb') as out:
    out.write(gzip.compress(data, compresslevel=9, mtime=0))
print(len(words), 'words')
