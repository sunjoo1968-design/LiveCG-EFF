# v0.13.0 UI Design
제작자 SunjooAn · 2026-11-01

1. Preview toolbar와 Area toolbar는 높이36~40px의 수평 StackPanel+ScrollViewer, 세로 스크롤/Wrap 금지. 그리기 선택도형·연결방식을 펼침 없이 표시. 좌우 스크롤 버튼·마우스 휠로 좁은 폭 대응. 미리보기 옵션도 한 줄로 유지한다. 소프트 폭은 compact slider 한 줄, 상세는 Popup(출력/도구줄 높이 변화 없음).
2. 하단의 AreaPinCheck/AreaToolsExpander/분리/접기 동작과 배치 복구를 제거한다. 표시/잠금은 레이어 행에서 조절한다. 원본/효과 창 분리는 유지한다. 구버전 AreaTools 배치 필드는 무시한다.
3. EffectsPanel Grid에 헤더/레이어 패널/높이 구분선/기존4탭 배치. 기본 레이어 패널240px, 내부 가상화 수직 스크롤. 기존 RegionList의 EffectRegion 선택 타입은 유지하고 DataTemplate을 썸네일/그룹 접기/눈/이름/효과·마스크 수/잠금으로 바꾼다.
4. 이름 double-click/F2/우클릭 편집, 행 드래그 순서·드롭 표시. 같은 부모 레이어 순서를 바꾸며 그룹 변경은 명시 그룹 연결 메뉴 사용. 목록·타임라인·캔버스 단일 모델/선택을 공유한다. 잠금/숨김/이름/순서는 undo에 기록, 전체파일 원본 유지.
5. EffectsWidthSplitter와 LayerHeightSplitter로 조절. WorkspaceState.RightWidth/LayerPanelHeight 유한값 정규화·화면 크기 clamp, 선호 크기 저장. 효과 창 분리/복귀/배치 초기화에서도 같은 컨트롤을 사용한다.
6. 실제 WPF에서 여러 폭의 한 줄·팝업·레이어 목록/이름·눈/잠금/순서·100개 내부 스크롤·크기 저장/복구·구버전 배치 무시를 검증한다. 기존 Core/출력 회귀와 배포 실행 검사. 버전0.13.0+SunjooAn. GitHub 공개 배포와 완료 통보는 기존 승인 방식 유지.
