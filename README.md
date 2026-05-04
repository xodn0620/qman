# Q-Man (.NET WPF)

매뉴얼 문서를 로컬에 인덱싱하고, 선택한 카테고리 범위에서 RAG 질의응답을 하는 Windows 데스크톱 앱입니다.

## 소스 위치

- 솔루션: `QMan.sln` (저장소 루트)

## 라이선스·고지

- **본 프로그램(Q-Man)**: 저장소 루트 [`LICENSE`](LICENSE) (MIT, 저작권 표기는 해당 파일 기준)
- **서드파티·런타임·sqlite-vec·PdfPig 커스텀 등**: [`THIRD_PARTY_NOTICES.txt`](THIRD_PARTY_NOTICES.txt) 참고  
  배포 ZIP/설치본에는 `LICENSE`와 `THIRD_PARTY_NOTICES.txt`를 **함께 포함**하는 것을 권장합니다(빌드 시 `QMan.exe` 출력 폴더로 자동 복사).

## 실행 (개발)

```powershell
cd C:\cursor\qman
dotnet run --project QMan.App\QMan.App.csproj
```

## 설정

- 기본 경로: **`%USERPROFILE%\qman\config.json`** (없으면 저장소 루트 `config.json` 등 포터블 탐색 순서는 `AppConfig.Load` 참고)
- 환경 변수(`OPENAI_API_KEY`, `SMQ_LLM_*` 등)로도 덮어쓸 수 있습니다. (`QMan.Core`의 `AppConfig` 참고)
- sqlite-vec DLL: 빌드에 임베드한 경우 첫 실행 시 `QMan.exe` 옆 `native\`에 풀림. 로드 실패 시 코사인 폴백 검색을 사용합니다.
- 데이터/설정 기본 폴더: `%USERPROFILE%\qman\` (`config.json`, `data\qman.db` 등)
- 이미지 OCR: Windows 10 이상의 **Windows.Media.Ocr**을 사용합니다. **설정 > 시간 및 언어 > 언어**에서 한국어·영어 등에 **광학 문자 인식(OCR)** 옵션이 설치되어 있어야 합니다.
- 저장소 루트의 `config.json`이 있으면(또는 `portable.flag`) 포터블 모드로 동작할 수 있습니다.

OpenAI를 쓰려면 `config.json`에서 `provider`를 `openai`로 바꾸고 `openAiApiKey`에 키를 넣거나, `OPENAI_API_KEY` 환경 변수를 설정하세요.

## 빌드

```powershell
dotnet build QMan.sln -c Release
```
