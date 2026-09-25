AUTOHUNT NEW WINDOW V4

이번 버전은 실제 새 자동사냥 UI 창 디자인/구조 테스트입니다.

이전 V1/V2와 다른 점:
- 랭킹창 전체를 복제하지 않습니다.
- MainButtonUI 안에 자동사냥 창을 붙이지 않습니다.
- AutoHuntSettingsUI.source.xml은 우리가 직접 만든 독립 레이아웃입니다.
- 설치 때 게임의 기존 버튼/Window 속성 중 렌더링에 필요한 스킨 속성만 읽습니다.
- 470x320 전용 배경 PNG(910101.png)를 새로 생성하여 Image00.idx/pak에 등록합니다.
- AutoHuntSettingsUI.xml을 별도로 컴파일하여 UI.idx/pak에 등록합니다.
- 기존 V1/V2에서 MainButtonUI에 삽입한 AutoHunt 테스트창은 제거합니다.

현재 목적:
1. 새 창 자체가 월드에서 독립 UI로 로드되는지 확인
2. 크기/배치/한글/디자인 확인

버튼 동작은 이 단계에서는 Dummy입니다.
창 표시가 정상 확인되면 다음 단계에서 AUTO 버튼 열기/닫기 및 서버 저장/시작/중지를 연결합니다.

실행:
게임과 접속기를 종료한 뒤 UPDATE_AUTOHUNT_V4.bat 실행.
