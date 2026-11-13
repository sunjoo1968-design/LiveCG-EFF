# 프로젝트 구성

현재 버전1.5.4 · 제작자 SunjooAn

- src/LiveCG.App: Windows WPF 편집기, 창·타임라인·영역·색상·모션 UI
- src/LiveCG.Core: 효과·그룹·마스크·프레임 규칙·WebM/OBS 브라우저 출력
- src/LiveCG.Core/Browser: 서버 없이 동작하는 OBS 로컬 패키지 런타임
- tests: 생성한 테스트 이미지로 수행하는 코어/WPF 회귀 검사
- tools: 환경 준비·배포·패키지 검사·Chromium/OBS 통합 검사
- docs: 현재 버전 설계·완료 보고·검증 요약·다음 버전 제안
- licenses: 사용 중인 외부 구성 요소 고지

실행용 ZIP은 GitHub Releases에 배포합니다. .tools, bin/obj, artifacts, dist는 재생성 파일이므로 Git으로 관리하지 않습니다. 원본 자막 폴더와 개인 작업·설정은 로컬에만 보관합니다.
