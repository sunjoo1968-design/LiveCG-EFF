# 외부 구성 요소

LiveCG-EFF 제작자: **SunjooAn** · 프로그램 버전: **0.10.0**

아래 구성 요소의 제작자·저작권은 각 원저작자에게 있습니다. 프로그램 제작자 표시는 이 표기를 대체하지 않습니다.

| 구성 요소 | 제작자 / 프로젝트 | 라이선스·소스 |
|---|---|---|
| .NET 10 / WPF | Microsoft 및 .NET 기여자 | MIT 및 외부 고지; 배포 licenses/dotnet/LICENSE.txt, ThirdPartyNotices.txt; https://github.com/dotnet/runtime / https://github.com/dotnet/wpf |
| SkiaSharp 3.119.2 / Skia | Microsoft, Google 및 기여자 | MIT / BSD 및 외부 고지; licenses/SkiaSharp-MIT.txt, SkiaSharp-Native-THIRD-PARTY-NOTICES.txt; https://github.com/mono/SkiaSharp/tree/v3.119.2 / https://skia.googlesource.com/skia/ |
| SkiaSharp WPF Views / Desktop Common | Microsoft 및 기여자 | MIT; licenses/SkiaSharp-LICENSE.txt; https://github.com/mono/SkiaSharp |
| OpenTK / GLWpfControl | OpenTK 및 기여자 | MIT; licenses/OpenTK-LICENSE.md, GLWpfControl-LICENSE.md; https://github.com/opentk/opentk / https://github.com/opentk/GLWpfControl |
| FFmpeg 9.0.2 essentials build | FFmpeg 개발자, Windows 빌드 Gyan Doshi | GPL v3; app/tools/ffmpeg/LICENSE 및 README.txt 원문 유지; https://www.gyan.dev/ffmpeg/builds/ |

FFmpeg는 별도 프로세스로 사용하며 변경하지 않은 실행 파일·라이선스·제공자 README를 함께 제공합니다. 해당 제공자 README의 FFmpeg 소스는 https://github.com/FFmpeg/FFmpeg/commit/946fcce07b 입니다. libvpx를 포함한 내부 외부 라이브러리는 제공자 README와 ffmpeg -L, ffmpeg -buildconf 출력을 참조하세요. 외부 재배포 시 해당 바이너리의 라이선스와 대응 소스 제공 조건도 함께 유지해야 합니다.

현재 배포에는 자동 탐지 기능과 Tesseract·Leptonica·언어 모델을 포함하지 않습니다. 저장소 licenses의 해당 고지 파일은 과거 사용 구성 요소의 저작권 고지로만 보관합니다. 루트 실행 도우미는 Windows의 .NET Framework4.x를 사용하고 해당 런타임을 별도 재배포하지 않습니다. LiveCG SVG/ICO 아이콘은 이 프로젝트에서 제작했습니다.

개발 도구 URL과 해시는 tools/dependencies.json에 고정합니다. 앱은 PNG 렌더링과 변환을 로컬에서 수행합니다.

