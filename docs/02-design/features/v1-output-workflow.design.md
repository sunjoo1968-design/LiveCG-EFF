# v1.0.0 출력 작업 Design
2026-11-01 · SunjooAn

1. PlaybackSections에 LoopRest(0~30초), RestMode(Original/Hold/Transparent). 누락 시0으로 이전 문서 호환, 새 앱/가져오기 기본은1초 Original. PlaybackPlan 정수 RestFrames/CycleFrames, WebM 길이 Intro+Loop*N+Rest*(N-1)+Exit. 마지막 쉬기 생략. 방송 시계는 loop+rest 반복하며 퇴장 요청은 현재 효과가 끝나는 즉시(쉬는 중이면 즉시) 퇴장한다.
2. 쉬기 플래그와 원본/정지/투명 상태를 네이티브 출력·방송 미리보기·JS 런타임에서 공유한다. 원본 모드는 숨김/마스크/등장 상태와 무관하게 원본 이미지를 그대로 표시한다. 마지막 모습은 반복 마지막 프레임, 투명은 알파0. 구간 편집 시간과 최종 출력 시간은 분리한다. 타임라인 구간띠에 반복 뒤 쉬기 길이/방식 표시.
3. 좌측 출력 5분류: 형식, 반복/길이(횟수·쉬기·방식·선택일괄·1회확인), 품질, 저장, 실행/진행. 구간 창과 동일 PlaybackSections 저장, 변경마다 프레임 단위 총길이/선택파일 요약 갱신. 출력 예정 목록에서 파일명/경로/길이와 충돌 확인. 확인창은 명시 버튼으로 열어 기존 직접출력 흐름 유지.
4. BrowserExporter는 원본명_index.html/원본명_preview.html 주 진입점과 manifest entry/preview 필드. index.html/preview.html은 호환용 동일 내용 페이지로 유지해 이전 자동화/검증 경로 지원, 새 안내/위치열기는 명명 진입점을 사용한다. HTML/JS 문자열에는 이름을 JSON으로 인코딩하며 파일명 사용불가문자/경로 탈출 차단.
5. SourceImage 공통 읽기: PNG/JPG/JPEG/BMP/정적WebP, 실제 codec 검사·애니메이션 거부·JPEG EXIF 회전. 렌더/캔버스/스포이드/썸네일/분리/모션/출력 모두 같은 정규화 이미지 사용. 패키지 original.png는 디코드 결과를 실제PNG로 저장. 원본을 수정하지 않으며 저장된 크기 불일치 감지. 동명 입력 출력충돌 사전검사.
6. 새 영역 ContentOnly/CutoutEnabled=true, 기존 문서의 ContentOnly=false는 유지. 새 기본 설정은1초 쉬기이며 사용자 기본값 저장에 포함한다. 패널 폭·편집/타임라인 분할은 사용자 조절 잠금, 전체창 리사이즈는 자동배치. 레이어 높이 splitter·스크롤·높이 기억은 유지. 분리창의 크기조절도 잠그고 복귀 유지.
7. Core: 프레임 매핑/쉬기 경계/퇴장/직렬화/EXIF/포맷/동명/실제WebM·명명HTML. WPF: 좌측·구간 동기화/Undo/일괄/초기값/길이/패널잠금·레이어높이. Chromium·OBS 실프로그램전환/원본쉬기/렌더비교. v1.0 패키지 기동/해시 후 공개배포, 최신만 남기고 이전 dist/임시/Release/배포태그 정리. Git 기록과 개인 자료 보존.
