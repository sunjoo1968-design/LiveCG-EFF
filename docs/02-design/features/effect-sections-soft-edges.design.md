# v0.11.0 효과 기준 구간·부드러운 경계 — Design
제작자 SunjooAn · 2026-10-31

1. EffectClip.Section(자동/등장/반복/퇴장), PlaybackSections.ByEffects와 nullable ExitEnabled. 누락 필드는 기존 시간 방식/퇴장 유지. 새 작업만 효과 방식 및 퇴장 끔. 연결은 clip ID가 아닌 클립 속성으로 복사·템플릿에 보존. 섹션 resolver가 유효 클립 끝을 정수 프레임에 맞추어 매번 계산한다. 구간 창에서 다중 선택 배정·자동 정렬, 정밀 초/프레임·경계분할 지원. 타임라인 우클릭도 배정한다.
2. RegionSoftness 값 레코드: Enabled/Width/Strength/Direction/SeparateSides/Top·Right·Bottom·Left. RegionRaster가 원래 경로를 유지하며 외부 여백을 포함한 crop rect와 공통 알파 마스크를 생성한다. 기본 0은 기존 렌더와 동일하다. 캐시 키/스냅샷/유효성 검사 포함. RGB 블러 없이 알파만 경계에서 감쇠한다.
3. ContentOnly 선택: 단색 배경의 경계 연결 성분만 제거하고 원본 RGB와 내부 닫힌 글자색은 보존한다. 기존 ObjectExtractor 재사용 또는 공통 래스터에서 수행. 원본 위치는 배경색으로 복구한다. 소프트와 함께 사용 가능. 자동/직접 배경색 및 허용오차 슬라이더. 일반 이미지의 숨은 배경 재구성은 하지 않는다.
4. EffectScene의 단순/그룹/하위영역 및 브라우저 자산은 같은 crop/mask/내용 추출을 쓴다. 시간 없는 소프트는 PNG 자산에 반영하고 브라우저는 기존 변환을 적용한다. 배경 복구와 움직이는 이미지의 알파가 다름을 유지해 잔상 방지. 정지 상태 최적화가 소프트/배경고정을 우회하지 않게 한다.
5. MainWindow 경계 속성은 마우스 슬라이더+정밀 입력, undo와 즉시 프리뷰. PreviewControl 안/밖 경계와 폭 손잡이; 선택표시 숨김에는 보조선 모두 숨김. 배경 비교는 출력에 영향 없는 검정/흰색/체크무늬/사용자 이미지, 선택형 좌우 비교. 반복 연결 확인 창은 공통 Scene으로 마지막/첫 프레임을 비교한다.
6. schema6의 선택 필드로 이전 JSON 호환, 이전 파일 퇴장/시간 옵션 보존. 새 버전0.11.0+SunjooAn 모든 창/README/배포. 단위·픽셀·WPF 통합·Chromium 출력 비교·패키지 검사로 검증한다.
