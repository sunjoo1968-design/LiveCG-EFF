# v0.10.0 완료 보고 · 방송 효과 개선
2026-10-31 KST · 제작자 **SunjooAn** · PDCA broadcast-polish · 설계22/22(100%)

공개 정리: 이후 사용자 요청에 따라 이전 배포본·과거 버전 문서·임시 검증 자료·빌드 캐시를 제거했다. 아래 artifacts 경로는 당시 검증 이력이며, 보존한 결과는 [검증 요약](../verification/v0.10.0/summary.json), [패키지 검사](../verification/v0.10.0/package-report.json), [렌더 비교](../verification/v0.10.0/render-comparison.json)에 있다. 최신 배포 파일의 SHA256은 정리 전후 동일하다.

## 반영한 요청
- OBS Studio Mode에서 프리뷰의표시와프로그램의활성을분리했다. 프로그램진입시0초재시작, 중복활성화무시, 로딩전이벤트/새로고침세션상태/프로그램직접추가를보완했다. 구간설정라벨은 **OBS 프로그램 전환 시 처음부터 재생**이다.
- 투명여백스윕과Zoom완료후Ripple흰경계를최소재현했다. native red2→green2, 원형원본경계최대차255→0. 스윕을실제객체알파에한정, 정지장식은원본을직접보존한다. 브라우저도같이수정했다.
- **최약/약/중/강/최강**5단계. 기존enum0/1/2및세기유지, UI매핑분리. 최약.18·기존약.55·중1·강1.65·최강2.4.
- **왼쪽부터 펼치기(좌→우크롭등장), 위에서 등장, 밑줄 형광펜**추가로20종.
- 장식색상/투명도·색상표·원본스포이드·기본색복원. 메인/제작실/에셋/저장/복사/Undo/브라우저연동.
- 새모션은숫자샘플·빈키로시작. 명시예제버튼만예제불러오기. CropTop/Bottom/Left/Right4채널을추가해총12행, 슬라이더/프레임키/브라우저/마스크동작을연결했다.
- 우측4탭은큰테두리·높은대비·고정2×2순서이다.

## 검증
**Core368 + 실제WPF301 + Chromium40 + VM시계5 + 실제OBS33통합14 + 프레임비교24 =752/752 통과**. Debug0경고0오류·Release배포성공. 기존회귀포함.

실제OBS33.0.0을프로젝트내독립portable복사본에서기동했다. 사용자OBS설정/원본과분리했고방송·녹화는하지않았다. 프로그램전환2회/프리뷰대기후전환/프로그램새로고침/프로그램직접추가/CEF캡처를검증했다. 캡처소스는videoActive/videoShowing을확인하여StudioMode private scene copy의잘못된소스를캡처하는오류를배제했다. 테스트OBS/서버는종료했다.

Chromium22장+실제OBS2장의네이티브대비24장: premultiplied RGBA평균오차최대 **0.314/255**, 채널차20초과픽셀최대 **0.314%**. OBS소스스크린샷의알파표현을정규화했다. 실제OBS빛스윕평균0.064/255·크롭0.051/255, 큰차이픽셀0%. 비교기준평균<3/255·차이픽셀<3.5%전부통과.

로그 artifacts/v010-{build,core,ui,browser,program-clock,obs,browser-comparison,publish,package}.log, JSON v010-obs-results/v010-browser-comparison/v010-package-report. red/green기록 v010-debug. UI broadcast-effect-tabs.png, broadcast-crop-studio.png. 원본/실제/diff는 v010-core/*.obs에있다.

## 배포
- 프로그램: **dist/LiveCG-EFF-0.10.0/LiveCG-EFF.exe**
- ZIP: **dist/LiveCG-EFF-0.10.0-win-x64.zip**,171.6MiB·437항목
- SHA256: **25D2BB4953B3DEBAFAF0CA2396CD1040A3D6C85FF15462BF09041F4D846A88C4**
- 기존OBS패키지용: **dist/LiveCG-OBS-runtime-0.10.0.zip**(9,540bytes·player.js/사용안내2파일), 원본JS와해시일치검사.
- 실제루트launcher→메인창 SunjooAn/v0.10.0, 아이콘7크기/앱일치, 런타임·FFmpeg·외부라이선스/ZIP DLL해시를검사했다. 이전5개배포ZIP해시보존. 원본PNG/영상수정없음.

기존패키지는새앱에서재출력하거나런타임패치의player.js를교체하고OBS현재페이지캐시새로고침을한다. 새효과/강도/색/크롭은새로출력해야한다. WebM파일자체는새앱에서다시출력한다. 새기능이있는작업은v0.10이상에서사용한다.

## 동작 조건
처음로드된페이지에OBS가초기active를알리지않을때는프리뷰에서재생될수있으나다음프로그램활성화에서처음부터재시작한다. 이미비활성임을확인한소스는첫프레임대기한다. 같은브라우저소스를여러장면이공유해프로그램에서계속활성화한경우새진입이벤트가없으므로장면별별도소스또는R/API를사용한다. Canvas/Skia의AA·블러·숫자폰트는소폭차이가있고브라우저실시간프레임수는PC성능에따른다. 원단위숫자보호/사운드는이전결정대로추가하지않았다.

완료통보는승인한이채팅+Windows팝업/알림음, 기록 artifacts/v010-completion-alert.json. 재개자동화 livecg-v0-9(이름LiveCG v0.10재개)는완료후PAUSED. 완료한코드/배포/알림중복실행금지.
