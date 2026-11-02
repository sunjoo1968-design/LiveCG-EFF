# v1.2.0 설계
2026-11-02 · SunjooAn · travel-routes-separation

1. 숫자: DigitCells는 선택 영역 내부 테두리의 배경 추정을 우선 사용하고 원래자리 채움색과 분리해 보관한다. 투명 마스크는 alpha, 불투명은 배경 대비의 열 프로젝션을 사용한다. 붙은 폭넓은 run은 골/비례 기준으로 분할, 작은 쉼표는 칸에서 제외. EffectRegion.DigitDividers(정규화 경계)를 저장하고 숫자 칸 편집창에서 드래그/추가/삭제한다. 원본 자릿수 이미지로 정착. 캐시는 분리/수동경계 변경에만 갱신한다.
2. TextSeparation: 기존 정확 모드 보존. TextSeparationSettings(RangeMode,BackgroundColors,ColorTolerance,InkTolerance) 추가. 다중 배경과 보호색 범위를 구분, 잉크 근처 혼합색은 재합성 가능한 coverage로 보존하며 색 범위 제거가 획 경계로 침범하지 않도록 보호. 애매하면 보존. 기존 원본 alpha 우선. 일반 Object와 구분.
3. 정밀창: 비동기 debounced 계산(드래그 중150ms), 최신 세대만 UI 적용, 적용 시 최신 결과를 기다림. 자동 기본 마스크와 수동 include/exclude patch를 별도 보존. 상태 Snapshot에는 옵션·색·대상·마스크·Foreground·수동 patch 포함. 드래그 시작/종료 한 Undo. 버튼 상태/작업명·CtrlY. 메모리 제한으로 마지막 상태까지 삭제하지 않음. 변경 없는 선택/이동에는 계산 없음.
4. 성능: 정지 프레임과 조절선 캐시, 소프트 마스크/거리장 재사용, 비동기 분리는 별도 스냅샷 소유권/폐기, 원본 출력 품질 유지. 반복되는 원본 크기 복사·PNG 직렬화·전체 선택 history 생성 비용 측정 및 줄이기. 진단 Counter로 선택만 했을 때 matte 계산0 검증.
5. OBS: UI 항목 순서와 enum 값은 별도 매핑(직접 SelectedIndex cast 제거), 신규 VideoOptions.Output=ObsBrowser. 방송 미리보기 첫 항목·기본, 기존 작업 출력 유지. WebM 전용 횟수/총 길이는 OBS-only 시 숨기고 공통 쉬기는 유지.
6. 여행: EffectKind.TravelRoute 추가(기존 enum 뒤), EffectClip.Route(깊은 복사/검증/에셋 저장). RouteDefinition은 정규화 waypoint와 cubic in/out handle, 각 지점 이름/정차/구간 이동시간/아이콘(Plane/Car/Camper/Bicycle/CustomPNG), 경로 스타일/선색·두께·불투명·외곽/예정선·도착강조·크기·방향·보조애니/반복. 공통 샘플링된 경로와 이동 시간표를 native/JS에 사용하여 위치·궤적·방향 일치. 기본 아이콘은 프로젝트 자체 vector를 PNG로 자산화해 native/browser 공통 사용. 사용자 PNG/WebP를 검증한 PNG에 정규화하여 데이터에 포함(최대512x512). 기본 정차1초, 이동구간2초. 총 길이<=300초. 마지막 상태 유지, 반복은 클립내반복/OBS반복구간과 통합.
7. 지도창은 원본지도 확대·축소·pan/포인트 클릭·drag/곡선손잡이/삽입·삭제·순서·UndoRedo·리스트 속성·시간 재생·스타일/아이콘·정차 일괄·경유점별교통·에셋 저장/타임라인 삽입. 선택된 지도영역에 경로 overlay decoration 추가, 지도 원본 불변. 재편집 버튼/내 여행 에셋 연결. 타임라인 길이는 이동+정차 자동, split/source timing 유지. WebM/OBS shared assets, 시작과 멈춤·정착·방향·mask/group 회귀.
8. 실패 재현→수정→회귀 Core/WPF→Chrome/실제OBS 렌더 및 전환→성능→패키지→공개 해시·태그 검증 후 이전v1.1.0/임시만 정리. 사용자 자료는 artifacts 로컬 분석에만 사용, 공개 검증은 합성 자료. checkpoint 주기 저장.
