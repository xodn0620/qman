# QMan 프로젝트 라이센스 검증 리포트

검증 일시: 2026-05-04
검증자: AI Assistant (Cursor)
검증 범위: 전체 소스코드 및 의존성 패키지

## 요약

✅ **모든 라이센스 이슈 해결 완료**

- SixLabors.ImageSharp 상용 라이센스 이슈 → **제거됨**
- NPOI 의존성 → **ExcelDataReader로 교체**
- sqlite-vec 라이센스 파일 → **추가됨**

## 1. 프로젝트 자체 라이센스

**QMan 프로젝트**: MIT License
- 파일: `LICENSE`
- Copyright: 2026 Shinhan DS, TaeWoo Yong
- 상태: ✅ 명확히 정의됨

## 2. 직접 의존성 패키지 (직접 참조)

| 패키지 | 버전 | 라이센스 | 상태 |
|--------|------|----------|------|
| DocumentFormat.OpenXml | 3.5.1 | MIT | ✅ |
| ExcelDataReader | 3.7.0 | MIT | ✅ |
| ExcelDataReader.DataSet | 3.7.0 | MIT | ✅ |
| Microsoft.Data.Sqlite | 10.0.5 | MIT | ✅ |
| System.Net.Http.Json | 10.0.5 | MIT | ✅ |
| System.Security.Cryptography.Xml | 8.0.3 | MIT | ✅ |
| System.Text.Encoding.CodePages | 8.0.0 | MIT | ✅ |
| System.Text.Json | 10.0.5 | MIT | ✅ |
| UglyToad.PdfPig | 1.7.0-custom-5 | Apache 2.0 | ✅ 최신 버전* |

*NuGet.org에서 사용 가능한 최신 버전. Apache 2.0 라이센스로 안전하게 사용 가능.

## 3. 전이적 의존성 패키지

**모든 전이적 의존성**:
- DocumentFormat.OpenXml.Framework 3.5.1 (MIT)
- SQLitePCLRaw.* 패키지들 (Apache 2.0)
- System.* 패키지들 (MIT - Microsoft .NET)

**상태**: ✅ 모두 MIT 또는 Apache 2.0

## 4. 네이티브 라이브러리

**sqlite-vec.dll**
- 위치: `native\sqlite-vec.dll`
- 라이센스: MIT / Apache 2.0 (이중 라이센스)
- 라이센스 파일: `native\sqlite-vec-LICENSE.txt` ✅ 추가됨
- 출처: https://github.com/asg017/sqlite-vec
- Copyright: 2024 Alex Garcia

## 5. 제거된 문제 패키지

### ❌ NPOI 2.7.6 (제거됨)
- 이유: SixLabors.ImageSharp 의존성
- 대체: ExcelDataReader 3.7.0

### ❌ SixLabors.ImageSharp 2.1.11 (제거됨)
- 이유: 상용 라이센스 조건 (연 매출 $1M 이상 시 구매 필요)
- 상태: **완전히 제거됨**

### ❌ SixLabors.Fonts 1.0.1 (제거됨)
- 이유: NPOI 제거로 함께 제거됨
- 상태: **완전히 제거됨**

## 6. 라이센스 호환성 매트릭스

| 프로젝트 라이센스 | 의존성 라이센스 | 호환성 |
|------------------|----------------|--------|
| MIT | MIT | ✅ 완벽 호환 |
| MIT | Apache 2.0 | ✅ 완벽 호환 |

**결론**: 모든 의존성이 MIT 프로젝트와 호환됨

## 7. 배포 시 포함 파일

배포 패키지에 반드시 포함해야 할 파일:

1. ✅ `LICENSE` (프로젝트 라이센스)
2. ✅ `THIRD_PARTY_NOTICES.txt` (서드파티 고지)
3. ✅ `native\sqlite-vec-LICENSE.txt` (sqlite-vec 라이센스)
4. ✅ `QMan.App\bin\Release\...\licenses\PdfPig-LICENSE.txt` (자동 복사됨)

**빌드 설정**: QMan.App.csproj에서 자동 복사 설정됨

## 8. 남은 권장 사항

### 우선순위: 낮음 (선택사항)

1. **licenses 폴더 정리**
   - 현재 위치: `QMan.App\bin\Release\...\licenses\`
   - 제안: 루트에 `licenses\` 폴더 생성하여 일관성 있게 관리

### UglyToad.PdfPig 버전 참고사항

- **현재 버전**: 1.7.0-custom-5 (NuGet.org 최신 버전)
- **GitHub 릴리스**: v0.1.14가 존재하지만 NuGet에 배포되지 않음
- **결정**: 현재 버전 유지 (NuGet 관리 용이, 78만+ 다운로드로 검증됨)
- **라이센스**: Apache 2.0 (문제없음)

## 9. 라이센스 준수 체크리스트

- [x] 프로젝트 라이센스 명시 (LICENSE 파일)
- [x] 서드파티 라이센스 고지 (THIRD_PARTY_NOTICES.txt)
- [x] 네이티브 라이브러리 라이센스 (sqlite-vec-LICENSE.txt)
- [x] 상용 라이센스 의존성 제거 (SixLabors 제거)
- [x] 모든 의존성 MIT/Apache 2.0 확인
- [x] 배포물에 라이센스 파일 포함 설정
- [x] .csproj에서 문제 패키지 제거 확인

## 10. 검증 결과

### ✅ **최종 판정: 라이센스 이슈 없음**

**근거**:
1. 모든 의존성이 MIT 또는 Apache 2.0 라이센스
2. 상용 라이센스 요구 패키지 완전 제거
3. 필요한 라이센스 고지 파일 모두 존재
4. MIT 프로젝트와 모든 의존성 100% 호환

**법적 리스크**: 없음
**상업적 사용**: 제약 없음
**재배포**: 라이센스 파일 포함 시 자유롭게 가능

---

## 부록: 의존성 패키지 전체 목록

```
DocumentFormat.OpenXml                 3.5.1         (MIT)
DocumentFormat.OpenXml.Framework       3.5.1         (MIT)
ExcelDataReader                        3.7.0         (MIT)
ExcelDataReader.DataSet                3.7.0         (MIT)
Microsoft.Data.Sqlite                  10.0.5        (MIT)
Microsoft.Data.Sqlite.Core             10.0.5        (MIT)
SQLitePCLRaw.bundle_e_sqlite3          2.1.11        (Apache 2.0)
SQLitePCLRaw.core                      2.1.11        (Apache 2.0)
SQLitePCLRaw.lib.e_sqlite3             2.1.11        (Apache 2.0)
SQLitePCLRaw.provider.e_sqlite3        2.1.11        (Apache 2.0)
System.IO.Packaging                    8.0.1         (MIT)
System.IO.Pipelines                    10.0.5        (MIT)
System.Memory                          4.5.3         (MIT)
System.Net.Http.Json                   10.0.5        (MIT)
System.Security.Cryptography.Pkcs      8.0.1         (MIT)
System.Security.Cryptography.Xml       8.0.3         (MIT)
System.Text.Encoding.CodePages         8.0.0         (MIT)
System.Text.Encodings.Web              10.0.5        (MIT)
System.Text.Json                       10.0.5        (MIT)
UglyToad.PdfPig                        1.7.0-custom-5 (Apache 2.0)
UglyToad.PdfPig.Core                   1.7.0-custom-5 (Apache 2.0)
UglyToad.PdfPig.Fonts                  1.7.0-custom-5 (Apache 2.0)
UglyToad.PdfPig.Tokenization           1.7.0-custom-5 (Apache 2.0)
UglyToad.PdfPig.Tokens                 1.7.0-custom-5 (Apache 2.0)
```

검증 완료 일시: 2026-05-04 17:02 KST
