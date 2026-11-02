"""Проверка слов, которую трудно обмануть. Порт TextGuard из Windows-версии.

Школьники и не только пишут «p o r n o», «p.o.r.n», «p0rn», «пoрнo» (латиница в русском слове). Каждый текст
проверяется в нескольких нормализованных видах.
"""

import re

_CYR = "авекмнорстух"
_LAT = "abekmhopctyx"
CYR_TO_LAT = dict(zip(_CYR, _LAT))
CYR_TO_LAT["ё"] = "e"
LAT_TO_CYR = dict(zip(_LAT, _CYR))
LAT_TO_CYR["n"] = "п"
LAT_TO_CYR["i"] = "і"
LEET = {"0": "o", "1": "i", "3": "e", "4": "a", "5": "s", "7": "t", "@": "a", "$": "s", "!": "i"}

_SEPARATORS = re.compile(r"[\W_]+", re.UNICODE)


def _map(text, table):
    return "".join(table.get(c, c) for c in text)


def collapse(text):
    """'p o r n o' -> 'porno': серии из трёх и более одиночных букв склеиваются."""
    out = []
    run = []

    def flush():
        if len(run) >= 3:
            out.append("".join(run))
        else:
            out.extend(run)
        del run[:]

    for token in _SEPARATORS.split(text):
        if not token:
            continue
        if len(token) == 1 and token.isalpha():
            run.append(token)
        else:
            flush()
            out.append(token)
    flush()
    return " ".join(out)


def variants(text):
    """Сам текст и его нормализованные формы (строчные, похожие буквы, leet, склеенные одиночные буквы)."""
    res = []
    if not text:
        return res
    seen = set()

    def add(v):
        if v and v not in seen:
            seen.add(v)
            res.append(v)

    lower = text.lower()
    add(text)
    add(collapse(lower))
    lat = _map(lower, CYR_TO_LAT)        # кириллические двойники -> латиница
    leet = _map(lat, LEET)               # 0->o, 1->i, 3->e ...
    cyr = _map(lower, LAT_TO_CYR)        # латинские двойники -> кириллица (для русских слов)
    for v in (lat, leet, cyr):
        add(v)
        add(collapse(v))
    return res


def matches(patterns, text):
    if not patterns or not text or len(text) < 3:
        return False
    for v in variants(text):
        for rx in patterns:
            if rx.search(v):
                return True
    return False
