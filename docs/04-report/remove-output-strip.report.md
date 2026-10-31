# LiveCG-EFF v1.0.1 수정 보고
2026-11-01 · 제작자 **SunjooAn** · PDCA remove-output-strip

사용자가 불필요하다고 지적한 타임라인 상단의 출력순서·등장·반복·쉬기 배지 줄을 삭제했다. 높이25px와 생성 코드를 함께 제거했다. 실제 구간 바·좌측 반복 설정·재생/출력 규칙은 유지한다.

Debug 경고0/오류0, 기존 WPF390/390 통과. 일반/최소 화면에서 해당 줄이 사라진 것을 확인했다. 렌더러 변경은 없어 v1.0.0의 Core/브라우저/OBS 검증을 참조한다. [현재 요약](../verification/v1.0.1/summary.json)·[패키지 검사](../verification/v1.0.1/package-report.json).

배포는 LiveCG-EFF-1.0.1-win-x64.zip이며 맨 위 LiveCG-EFF.exe를 실행한다. 공개 최신 ZIP 크기·SHA256/main·태그 검증 후 이전v1.0.0 Release/배포태그·로컬dist와 SDK/빌드/테스트 임시파일을 정리한다. 원본 자료·작업·사용자 에셋·Git 개발 기록은 보존한다.
