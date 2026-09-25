AUTOHUNT STANDALONE UI V3

이번 버전은 이전 V1/V2와 구조가 다릅니다.

핵심:
- MainButtonUI 안에 자동사냥 창을 추가하지 않습니다.
- HighRankUI 안에 자동사냥 창을 추가하지 않습니다.
- AutoHuntSettingsUI.xml이라는 별도 UI 파일을 새로 만듭니다.
- 그 XML을 클라이언트 UI 암호화 형식으로 컴파일합니다.
- UI.pak에 새 파일을 추가합니다.
- UI.idx에 AutoHuntSettingsUI.xml 항목을 새로 등록합니다.
- 기존 HighRankUI에서는 그림/버튼 스킨 속성만 참고하고 랭킹 기능/내용은 가져오지 않습니다.
- 이전 테스트에서 MainButtonUI에 넣었던 AutoHuntSettingsWindow/V2는 제거합니다.

고정 클라이언트:
D:\리니지1\리니지클라\클라

실행:
UPDATE_AUTOHUNT_V3.bat

테스트 목표:
게임 월드 진입 시 AutoHuntSettingsUI.xml이 별도 UI로 자동 로드되는지 확인합니다.
이 테스트가 성공하면 다음 버전에서 기본 숨김 + AUTO 버튼 열기/닫기 + 서버 기능 연결을 진행합니다.
