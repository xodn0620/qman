Q-Man — 오프라인 배포 패키지

■ 실행
  이 폴더에 있는 「QMan.exe」를 더블클릭하세요.

■ 데이터베이스
  qman.db 는 항상 QMan.exe 와 같은 폴더 아래 data\qman.db 만 사용합니다.
  data 폴더가 없으면 첫 실행 시 자동으로 만들어집니다. 다른 위치의 DB는 읽지 않습니다.

■ 벡터 검색 (sqlite-vec)
  빌드할 때만(소스): {저장소 루트}\QMan.App\native\sqlite-vec.dll (필요 시 vec0.dll 등 동일 폴더)
  → DLL은 QMan.exe 안에 임베드되며, 배포 ZIP에는 native 폴더가 생기지 않습니다.
  첫 실행 시 Windows가 SQLite 확장 로드를 위해 파일 경로가 필요하므로,
  QMan.exe 와 같은 폴더 아래 native\ 로 자동 풀립니다(data\ 와 같은 방식).
  publish 출력: {저장소 루트}\dist\QMan-portable-win-x64\
    dotnet publish QMan.App\QMan.App.csproj -p:PublishProfile=Portable-win-x64
  임베드 DLL이 없으면 앱은 동작하며 검색은 코사인 방식으로 동작합니다.

■ 질의 시 근거가 안 나올 때
  - 채팅 탭 왼쪽에서 선택한 카테고리와, 문서를 올린 카테고리(매뉴얼 관리)가 같은지 확인하세요.
  - 설정의 임베딩 모델을 바꾼 뒤에는 문서를 다시 업로드·인덱싱해야 할 수 있습니다.

■ 폴더 구성
  QMan.exe 옆: data\, native\ 가 생성될 수 있습니다.
