# Q-Man (.NET WPF)

매뉴얼 문서를 로컬에 인덱싱하고, 선택한 카테고리 범위에서 RAG 질의응답을 하는 Windows 데스크톱 앱입니다.

## 소스 위치

- 솔루션: `QMan.sln` (저장소 루트)

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
- Tesseract 언어 데이터: `%USERPROFILE%\qman\tessdata` 등 [DocumentParserService](QMan.Ingestion/DocumentParserService.cs)의 탐색 경로에 `eng.traineddata` / `kor.traineddata`를 두면 이미지 OCR이 동작합니다.
- 저장소 루트의 `config.json`이 있으면(또는 `portable.flag`) 포터블 모드로 동작할 수 있습니다.

OpenAI를 쓰려면 `config.json`에서 `provider`를 `openai`로 바꾸고 `openAiApiKey`에 키를 넣거나, `OPENAI_API_KEY` 환경 변수를 설정하세요.

## 빌드

```powershell
dotnet build QMan.sln -c Release
```
