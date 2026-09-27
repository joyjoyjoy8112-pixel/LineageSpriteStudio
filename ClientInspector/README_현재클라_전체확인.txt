Lineage Client Inspector V2.1

[서버팩 검색 방식 변경]
- 서버팩 ZIP 선택 방식 제거
- 상단 버튼을 '서버팩 폴더 선택'으로 변경
- 압축하지 않은 실제 서버팩 폴더를 선택
- 선택한 폴더의 모든 하위 폴더/파일을 재귀 검색
- 서버팩 파일은 실제 디스크 경로에서 직접 읽음

[통합 검색]
한 번의 통합 검색으로 아래를 같이 검색합니다.
- 클라이언트 실제 파일
- IDX/PAK 내부
- Navicat/SQL DB 백업
- 선택한 서버팩 폴더 전체

[서버팩 내용 검색]
- .java / .properties / .xml / .txt / .ini / .cfg / .conf
- .html / .htm / .json / .js / .css / .lua
- .bat / .cmd / .ps1 / .mf / .prefs / .sql 등
- 텍스트 파일은 실제 내용까지 검색
- .class / .jar / .dll / .exe 등 바이너리는 파일명만 검색

[서버팩 폴더 안 PSC]
- db/*.psc 같은 Navicat 백업 파일을 자동 인식
- zlib PSC는 통합 검색 시 자동 압축 해제
- 3000209 같은 실제 DB 값 검색 가능
- PSC를 별도로 꺼내거나 ZIP에서 선택할 필요 없음

[정확 검색]
- 숫자는 앞뒤 숫자 경계 검사
- 1~3자리 숫자는 DB/본문 검색 제외
- SQL은 INSERT/REPLACE 실제 데이터 행 중심
- 바이너리 HEX 우연 일치 제외
- 동일 결과 중복 제거
- 최대 500건 표시

원본 클라이언트, 서버팩 폴더, DB 백업은 수정하지 않습니다.
