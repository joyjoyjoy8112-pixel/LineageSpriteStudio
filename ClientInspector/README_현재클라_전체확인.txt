Lineage Client Inspector V2.0

[새 기능: 서버팩 ZIP 통합 검색]
- 상단에 '서버팩 ZIP 선택' 버튼 추가
- 현재 서버팩 ZIP을 선택하면 ZIP 내부 파일 전체를 왼쪽 목록에 추가
- 기존 통합 검색 한 번으로 아래를 동시에 검색
  * 클라이언트 실제 파일
  * IDX/PAK 내부
  * Navicat/SQL DB 백업
  * 서버팩 ZIP 내부

[서버팩 내용 검색]
- .java / .properties / .xml / .txt / .ini / .cfg / .conf
- .html / .htm / .json / .js / .css / .lua
- .bat / .cmd / .ps1 / .mf / .prefs / .sql 등
- 위 텍스트 형식은 실제 내용까지 검색
- .class / .jar / .dll / .exe 등 바이너리는 잡결과 방지를 위해 파일명만 검색

[서버팩 내부 PSC 자동 검색]
- 서버팩 ZIP 내부 .psc를 자동 인식
- zlib PSC는 자동 압축 해제
- 3000209 같은 실제 DB 값도 서버팩 안의 PSC에서 검색
- 검색 결과에 PSC 원본 오프셋과 주변 데이터를 표시
- 별도로 PSC를 꺼내서 선택하지 않아도 검색 가능

[정확 검색 규칙 유지]
- 숫자는 앞뒤 숫자 경계 검사
  예: 3000209 검색 시 13000209 / 30002090 제외
- SQL은 INSERT/REPLACE 실제 데이터 행 중심
- 경로/확장자 때문에 생기는 과도한 잡결과 제외
- 동일 결과 중복 제거
- 최대 500건
- 1~3자리 숫자는 DB/본문 검색 제외

[이 서버팩에서 확인된 구조]
- 전체 ZIP 항목 약 4,473개
- Java 소스 약 1,035개
- Class 약 1,435개
- TXT 약 910개
- XML/설정/이미지/JAR 포함
- db/*.psc 2개 포함

원본 클라이언트, 서버팩 ZIP, DB 백업은 수정하지 않습니다.
