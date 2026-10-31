# v1.0.1 출력 순서 띠 제거 Design
2026-11-01 · SunjooAn

- MainWindow.xaml의 TimelinePanel 상단 OutputSequenceStrip ScrollViewer25px 제거. 기존 복사/재생 버튼과 실제 타임라인 구간 바는 유지한다.
- MainWindow.Output.cs의 RefreshOutputSequence 호출/배지 생성 메서드와 관련 불필요 using 제거. 반복/쉬기 모델·좌측 요약 계산은 변경하지 않는다.
- 버전1.0.1, 현재 README/안내/화면 갱신. WPF 기존390 검사·최소창 시각 확인·Debug/Release·ZIP/DLL해시·루트 실행기 검증. UI 제거 변경으로 Core/브라우저 렌더 동작은 v1.0.0 검증 결과를 참조한다.
- 공개 main/배포태그/ZIP digest 검증 후 이전 배포/태그·로컬 dist 및 캐시 정리. 원본·작업·에셋·소스 기록 유지.
