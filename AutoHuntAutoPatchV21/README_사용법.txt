AUTOHUNT AUTO PATCH V2.1

수정 내용
- V2에서 패치가 정상 완료된 뒤에도 "Client patch failed."가 잘못 출력되던 문제 수정.
- 원인은 PowerShell 스크립트 호출 후 $LASTEXITCODE를 검사한 것이었습니다.
- V2.1은 실제 예외 발생 여부만 검사합니다.

사용
1. 게임/접속기를 완전히 종료합니다.
2. UPDATE_AUTOHUNT.bat 실행.
3. 아래 문구가 나오면 정상 완료:
   [AHGAME] PATCH COMPLETE
   [AUTO] Client patch complete.

참고
- 사용자가 방금 실행한 V2 로그는 이미 [AHGAME] PATCH COMPLETE까지 나왔으므로,
  실제 UI 패치는 성공했고 마지막 자동업데이터 판정만 잘못 실패한 상태일 가능성이 높습니다.
- V2.1은 같은 패치를 다시 실행해도 새 백업을 만든 뒤 다시 적용합니다.
