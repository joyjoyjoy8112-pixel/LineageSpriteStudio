Lineage Client Inspector V1.6

[핵심: 진짜 통합 검색]
검색어를 한 번 입력하고 '통합 검색'을 누르면 아래를 동시에 검색합니다.
- 클라이언트 실제 파일의 파일명/경로
- 클라이언트 실제 파일의 내부 바이트 문자열
- IDX/PAK 내부 파일명/경로
- IDX/PAK 내부 파일 내용
- Navicat .psc 백업 파일
- PSC가 ZIP 계열로 열릴 경우 PSC 내부 항목과 내부 내용
- SQL/텍스트 백업 내용

검색 결과의 '검색일치' 열에
- 파일명/경로
- 미리색인 내용
- 파일 내용 @ 0xOFFSET
형태로 어디에서 찾았는지 표시합니다.

[한 화면 통합 미리보기]
- HTML/TXT/XML/JSON/INI/JS/CSS/LUA/SQL → 텍스트
- PNG/BMP/JPG/JPEG/GIF/ICO/TIF/TIFF → 이미지
- SPR → 프레임
- PSC/DB 백업 → 읽을 수 있는 문자열 + HEX
- 기타 바이너리 → HEX + ASCII

[Navicat PSC]
- 상단 '나비캣 PSC 선택'
- .psc/.nb3/.sql/.db/.sqlite/.bak/.dump 선택 가능
- PSC가 표준 ZIP 계열이면 내부 항목을 목록에 추가
- 내부 SQL/문자열도 통합 검색 대상

[성능 처리]
- 실제 .pak/.idx 컨테이너 자체는 중복 대용량 검색하지 않고, 파싱된 내부 항목을 검색합니다.
- PNG/JPG/SPR/사운드의 픽셀/오디오 데이터는 내용 문자열 검색에서 제외하지만 파일명/경로는 검색합니다.

원본 클라이언트와 백업 파일은 수정하지 않는 읽기/검사/추출 전용입니다.
