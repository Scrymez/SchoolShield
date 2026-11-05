"""SchoolShield, редакция «Учитель» для Linux Mint: фильтр материалов 18+ и удобный Центр учителя.

Только стандартная библиотека Python 3.8+. Автор: Латипов С.А. Лицензия MIT.
"""

VERSION = "2.5.1"
APP_ID = "schoolshield-teacher"
AUTHOR = "Латипов С.А."
REPO = "Scrymez/SchoolShield"
SITE_URL = "https://github.com/Scrymez/SchoolShield"

# Локальный бесплатный классификатор NSFW: Yahoo open_nsfw (BSD-2-Clause) в формате ONNX. Те же константы, что в Windows-версии.
MODEL_FILE = "open-nsfw.onnx"
MODEL_SHA256 = "864bb37bf8863564b87eb330ab8c785a79a773f4e7c43cb96db52ed8611305fa"
MODEL_SIZE = 23590724
MODEL_URL = "https://github.com/Scrymez/SchoolShield/releases/download/ai-model-1/open-nsfw.onnx"
ONNXRUNTIME_PIN = "onnxruntime==1.20.1"
