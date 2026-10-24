# CLAUDE.md — 입찰노트 (BidNote)

이 저장소에서 작업할 때 먼저 읽는다. 진행 상황과 다음 할 일은 `PROGRESS.md`, 기획 문서는 `docs/`에 있다.
어느 PC에서 받아도 같은 맥락으로 이어서 작업할 수 있게, 계획·결정·함정은 PC 메모가 아니라 이 저장소에 남긴다.

## 무엇인가

나라장터(조달청) **공사 입찰 공고**를 data.go.kr API로 모아 보는 **개인 PC용 웹앱**.
금강입찰(kumkangbid.co.kr)을 벤치마크했다. 혼자 쓰는 앱이라 로그인이 없고 DB는 SQLite 파일 하나다.

- 기획: `docs/입찰정보서비스_개발계획서_v0.2.pptx` (v0.1에 `docs/…검토및개선사항.txt`를 반영한 판)
- 화면 시안 25개: `docs/입찰노트_화면시안.pptx` (메뉴 구성의 기준)
- 메뉴 안내 / 기능 확인서: `docs/입찰정보사이트_메뉴기능안내.pptx`, `docs/기능확인서.xlsx`

## 구조 (net10.0, `BidNote.slnx`)

| 프로젝트 | 내용 |
|---|---|
| `src/BidNote.Domain` | 엔티티(Notice, NoticeHistory, NoticeLicense, FavoriteNotice, MyBid …), `BidCalculator`, `RegionNames`, `ContractMethods` |
| `src/BidNote.Application` | 수집(`CollectService`), 목록·필터(`HomeSummary.cs`), 상세(`NoticeDetail`), 관심 공고·투찰 기록 저장 인터페이스 |
| `src/BidNote.Infrastructure` | EF Core SQLite(`BidNoteDbContext`, 마이그레이션), 나라장터 수집기(`G2B/`), 저장소 구현 |
| `src/BidNote.Web` | Blazor (정적 SSR + 필요한 곳만 `InteractiveServer`), 백그라운드 수집(`CollectorHostedService`) |
| `tests/BidNote.Tests` | xUnit |

## 명령

```bash
dotnet build -warnaserror          # [SupplyParameterFromForm] 속성에 초기값(= new())을 주면 BL0008 오류. 받는 속성은 nullable로 두고 접근 속성으로 쓴다 (CollectSettings.razor)
dotnet test
dotnet format --verify-no-changes
dotnet run --project src/BidNote.Web --launch-profile http   # http://localhost:5069 , /health 로 상태 확인
```

- **서버가 켜져 있으면 빌드가 DLL 잠금으로 실패한다.** 먼저 끈다: `Stop-Process -Name BidNote.Web -Force`
- 마이그레이션 추가 (로컬 도구 dotnet-ef, `dotnet tool restore` 먼저):
  ```bash
  dotnet ef migrations add <이름> --project src/BidNote.Infrastructure --startup-project src/BidNote.Infrastructure --output-dir Persistence/Migrations
  ```
  끝나면 `src/BidNote.Infrastructure/design-time.db*` 를 지운다. DB는 앱 시작 때 자동으로 마이그레이션된다.
- 이미 쌓인 데이터를 고칠 때는 마이그레이션 SQL(원문 `ApiRawResponses`에서 `json_extract`) 또는 `DatabaseInitializer`의 시작 시 보정을 쓴다.

## 비밀·로컬 파일 (GitHub에 없음 — PC마다 따로 둔다)

- API 키: `src/BidNote.Web/appsettings.Local.json` (`appsettings.Local.example.json` 복사해서 작성). **키 값을 출력·커밋하지 말 것.** 커밋 전 스테이징된 diff에 키 문자열이 없는지 검사한다. `appsettings.Local.json.*`(백업)도 gitignore.
- DB: `src/BidNote.Web/data/bidnote.db` (공고 원문·관심 공고·투찰 기록). PC끼리 동기화되지 않는다. 옮길 때는 서버를 끄고 `bidnote.db*`를 함께 복사.
- 설정 우선순위: appsettings.json → appsettings.Local.json → 환경변수 → 명령줄 (뒤가 이김).
- 같은 키로 두 PC가 동시에 수집하면 하루 호출 한도를 나눠 쓴다. 한 대만 켜 둔다.

## 나라장터 API (실제 호출로 확인한 사실)

- 주소: `https://apis.data.go.kr/1230000/ad/BidPublicInfoService/` (차세대 경로만 동작, 구 `…04/05`는 400)
- `getBidPblancListInfoCnstwk` 공사 공고 목록 — 공고차수 `bidNtceOrd`는 "000" 3자리, 날짜 "yyyy-MM-dd HH:mm:ss", 지역 `cnstrtsiteRgnNm`(시도+시군구), 계약방법 `cntrctCnclsMthdNm`("제한경쟁(3등급)" → "제한경쟁"으로 묶어 저장), 종류 `ntceKindNm`("취소" 포함이면 취소 공고). **기초금액은 없다.**
- 공고 목록의 `presmptPrce` = 추정가격(원, 문자열 정수). `Notice.EstimatedPrice`에 저장, 입찰 가능 금액 판정의 기준. 금액 상한 규칙은 `LicenseEligibility.AmountLimit` 한 곳.
- `getBidPblancListInfoLicenseLimit` 면허 제한 (`lcnsLmtNm`, "/0037" 같은 코드 꼬리는 뗀다)
  - **같은 `lmtGrpNo` 안은 "그리고", 그룹끼리는 "또는"** (2026-10-25 실데이터 확인: 그룹1 토목건축 / 그룹2 건축, 한 그룹에 일반소방(전기)+(기계)). 판정은 `LicenseEligibility` 한 곳.
  - `permsnIndstrytyList` = 그 줄 대신 인정하는 업종 "[토목건축공사업/0003]" (전체 줄의 약 24%). `NoticeLicense.Alternatives`에 '|'로 저장. `indstrytyMfrcFldList`(주력분야)는 아직 판정에 쓰지 않는다.
- `getBidPblancListInfoCnstwkBsisAmount` 기초금액 `bssamt`, 순공사원가 `bssAmtPurcnstcst`, A값 적용 여부 `bidPrceCalclAYn`
  - **A값** = 국민연금 `npnInsrprm` + 건강보험 `mrfnHealthInsrprm` + 노인장기요양 `odsnLngtrmrcprInsrprm` + 퇴직공제부금 `rtrfundNon` + 산업안전보건관리비 `sftyMngcst` + 안전관리비 `sftyChckMngcst` + 품질관리비 `qltyMngcst`(`qltyMngcstAObjYn`=Y일 때만). 환경보전비·하도급 지급보증수수료는 넣지 않는다. 항목 표는 `G2BCollector.CostFields` 한 곳.
- 호출은 `numOfRows=999`, `inqryDiv=1`. 하루 사용량은 `ApiUsages` 표 + `/status`, 한도 95%에서 자동 정지.
- 개찰 결과는 **낙찰정보서비스** `https://apis.data.go.kr/1230000/as/ScsbidInfoService/` (같은 키, `/as/` 경로만 동작. 구 경로는 400)
  - `getOpengResultListInfoCnstwk` 개찰 목록 — `inqryDiv=1` + 기간(개찰일시, 하루 약 340건). 1순위는 `opengCorpInfo` 한 칸에 "업체명^사업자번호^대표자^투찰금액^투찰률". 재입찰은 `rbidNo`로 여러 줄.
  - `getOpengResultListInfoOpengCompt` 업체별 순위 — `inqryDiv=4&bidNtceNo=&bidNtceOrd=`. 참가 3천 곳이 넘는 공고도 있다(999건씩 여러 페이지). **하한 미달·예가 초과·취소 업체는 `opengRank`가 빈 값**이고 비고 `rmrk`에 이유가 온다.
  - `getOpengResultListInfoCnstwkPreparPcDetail` 복수예비가격 15줄 — `inqryDiv=2&bidNtceNo=…`. 줄마다 같은 `plnprc`(예정가격)·`bssamt`가 온다. 실제 사정률 = plnprc / bssamt.

## 수집 규칙 (지키지 않으면 실제로 데이터가 깨졌던 것들)

- 같은 공고가 차수만 달리(000 → 001) 한 번에 온다 → 공고번호·차수 순으로 처리하고 이번 수집에서 본 공고를 기억한다 (UNIQUE 오류 방지). 더 낮은 차수는 무시.
- 원문(`ApiRawResponses`)은 새 공고·바뀐 공고일 때만 저장(해시 비교). 바뀌면 이전 상태를 `NoticeHistory`에 남긴다.
- **정정 공고가 기초금액을 지우면 안 된다.** 목록 API에는 기초금액이 없으므로 값이 올 때만 덮어쓴다.
- 지역은 짧은 시도 이름(`RegionNames.ToShort`: 대전광역시 → 대전), 원래 이름은 `SiteRegionName`에 둔다.
- 기본은 가상 데이터 모드. `G2B:UseFakeData=false`면 시작 때 가상 공고(SourceSystem=FAKE)를 지운다.

## 화면·코드 함정

- Razor에서 `@expr건`은 식별자로 읽힌다 → `@(expr)건`.
- 목록의 별(관심 공고) 버튼은 표 전체를 대화형 구성요소 하나(`NoticeTable`)로 둔다. 줄마다 따로 두면 200줄에서 연결이 끊겼다.
- 메인 `/`는 쿼리 없이 열면 마지막 검색 조건(`LastSearches`)으로 넘어간다. 필터를 비우는 링크는 `/` 대신 `/?reset=true`를 쓴다. 조건 비교·저장은 `SearchQuery.Normalize`를 거친다(새 필터를 추가하면 여기에도 넣는다).
- 메인 필터는 GET 폼 안에 있다. 그 안에 대화형 입력칸을 두면 Enter가 검색을 제출하므로 `form="없는id"`로 폼에서 떼어 둔다(`SavedSearchPanel`).
- 디자인: 인천시청 누리집 참고 (Noto Sans KR, #0053B5 파랑 · #07296A 남색 · #F7B02A 노랑).

## 작업 규칙

- 답변은 한국어, 결론 먼저 짧게.
- 코드 변경 후 build(-warnaserror) · test · format 검사 셋 다 통과시킨다. 버그 수정 때 실패 테스트를 먼저 보여 줄 필요는 없다.
- 커밋·push는 사용자가 요청할 때. 기능 하나 = 커밋 하나.
- 파일 삭제, 레지스트리 등록, 외부 쓰기 같은 되돌리기 어려운 일은 먼저 확인받는다.
- 실행용 vbs·시작 화면(`실행파일/`)은 저장소 밖(`D:\01. 클로드\01.k\실행파일`)에 있고 경로가 그 PC 기준이다.
- 작업을 마치면 `PROGRESS.md`의 진행 상황과 다음 할 일을 고쳐 둔다.
