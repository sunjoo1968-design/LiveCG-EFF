# v0.10 검토 · broadcast-polish · SunjooAn
2026-10-31 KST · 설계22/22 구현(100%).

OBS프로그램 전환/중복이벤트/로드레이스/일반브라우저/초기unknown/새로고침은 실제runtime VM5개 및 독립OBS33 통합14개로 검사했다. 초기페이지의active 미통보와StudioMode의private scene copy를 실제OBS에서 확인해 대기/자동재생을 보완하고 실제프로그램소스의videoActive/videoShowing을확인했다. 사용자OBS설정과분리한portable복사본이며송출·녹화는하지않았다.

첨부영상을읽고투명여백스윕과Zoom다음Ripple원형경계를최소PNG로재현했다. native red2실패→green2통과, 원형경계최대채널차255→0. 스윕은실제객체알파에한정하고정지장식은원본픽셀을보존한다. BrowserCanvas동일규칙. OBSCEF의실제스윕/크롭소스캡처도대비했다. OBS GetSourceScreenshot의반투명RGB는premultiplied로반환되므로이표현을정규화하여비교했다(예:cyan RGB0/120/120·alpha120, canvas는RGB0/255/255·alpha120). 제품색을변경하는보정이아닌검사표현변환이다.

강도5단계는enum의기존0/1/2유지·UI순서별도매핑, 원래Power.55/1/1.65보존·최약.18/최강2.4추가. 새효과3개와Color는모든출력/저장/복사/Undo에연결했다. NewMotion빈키/숫자샘플, 4crop채널(총12)·제작실슬라이더/키/마스크모션/브라우저 렌더를확인했다. 4탭은UniformGrid+ContentPresenter로선택에따라순서가바뀌지않는고정2×2로바꿨다.

코어368, WPF301, Chromium40, 시계VM5, 실제OBS33통합14, native대비캡처24 =752검사. 최종결과 artifacts/v010-{core,ui,browser,program-clock,obs,browser-comparison}.log와완료보고서를참조한다. 기존5배포ZIP/원본보존은패키지검사로확인한다.

제약: 프로그램에같은OBS브라우저소스를계속공유해활성이유지되면새active진입이없다. 이경우장면별별도소스또는R/API사용을안내한다. 초기active미통보때프리뷰에서재생될수있으나실제프로그램활성화에서다시시작한다. 새패키지출력또는player.js교체/OBS캐시새로고침이필요하다. 외부송출/E2E플랫폼연동은하지않았다.
