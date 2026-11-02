#!/bin/sh
# SchoolShield, редакция «Учитель»: точка входа. Переменные окружения Python игнорируются (-E), чтобы pkexec/root не подхватил чужой код.
exec /usr/bin/python3 -E -B /usr/lib/schoolshield-teacher/run.py "$@"
