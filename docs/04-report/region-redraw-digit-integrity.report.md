# LiveCG-EFF v1.2.3 완료 보고
2026-11-02 · 제작자 **SunjooAn** · PDCA region-redraw-digit-integrity

영역을 삭제한 뒤 재지정하면 숫자 롤링이 잘려 보이던 문제를 수정했다. 실제 가격 이미지로 캐시를 대조했으며 캐시 잔존은 확인되지 않았다. 좁은 원래1에 넓은 임시0이 잘리는 문제와 분리 뒤 남은 붉은 무늬를 숫자 높이로 오인하는 문제를 구분해 해결했다. 원본 숫자 획을 깎지 않고 임시 숫자만 별도 공간에 맞춘다. 숫자 칸 편집과 같은 자동 경계를 사용하고 쉼표·원 단위는 고정된 원본으로 보존한다.

브라우저 패키지에는 앱의 임시 숫자 이미지를 내장해 글꼴 렌더링 차이를 줄였다. 최종 정착 프레임은 원본 숫자와 바이트 일치한다. 실제 자료의133,470/21%, 반복 삭제·재지정과 경계±2px, 정밀 분리·수동 숫자 칸·0~9폭·원본 복원·Chrome/실제OBS를 검증했다. 실제 이미지와 crop은 공개하지 않았다.

Core671 + WPF507 + Chromium183 + 프로그램 시계5 + 실제OBS68 + 렌더 비교155 = **1,589/1,589 통과**. Debug 경고0/오류0. 집중 Core76/WPF24와 실제 자료 선택 검사는 전체 수에 포함되며 중복 합산하지 않는다.

[검토](../03-analysis/region-redraw-digit-integrity.analysis.md) · [검증 요약](../verification/v1.2.3/summary.json) · [최종 패키지 검사](https://github.com/sunjoo1968-design/LiveCG-EFF/blob/main/docs/verification/v1.2.3/package-report.json)

배포는 **LiveCG-EFF-1.2.3-win-x64.zip**을 풀고 최상단 LiveCG-EFF.exe를 실행한다. app/사용안내/licenses를 함께 보관한다. 기존 작업은 그대로 열 수 있다. 기존 분리 마스크 자체가 잘못 저장되어 있으면 정밀 분리에서 다시 계산·보정 적용한다. OBS에 반영하려면 **브라우저 패키지를 새로 출력**한다. 별도 런타임 설치는 필요하지 않다.

공개 커밋·태그·최종ZIP 크기/SHA256 확인 뒤 이전v1.2.2 배포와 재생성 가능한 검사/개발 생성물을 정리한다. 사용자 원본·작업·에셋·실행 중인 앱·Git 기록을 보존한다. 최종 무결성 정보는 별도package-report/release-integrity JSON으로 제공한다.