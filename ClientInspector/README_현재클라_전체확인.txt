Lineage Client Inspector V1.7

[검색 노이즈 제거]
- 원시 바이너리 바이트 패턴은 통합 검색에서 제외
- .bin/.dat/.exe/.dll/.pak/.idx 등에서 우연히 숫자/문자 바이트가 겹치는 결과는 표시하지 않음
- MZ/PE 실행 파일의 HEX 같은 결과는 검색 결과에서 제외

[검색 대상]
- 파일명 / 경로
- HTML/TXT/XML/JSON/INI/CFG/JS/CSS/LUA/CSV/LOG/MD/YML/YAML/SQL 실제 텍스트 내용
- IDX/PAK 내부의 텍스트 파일 내용
- Navicat PSC 내부에서 텍스트로 판별된 내용
- PSC 내부 바이너리에서는 실제 ASCII 문자열로 분리된 의미 있는 문자열만 색인

[통합 검색]
검색어 한 번으로 클라이언트 + IDX/PAK + Navicat PSC를 동시에 검색합니다.
검색일치 열에는 '파일명/경로', '미리색인 내용', '텍스트 내용 @ 0xOFFSET'으로 표시합니다.

[미리보기]
검색 결과를 클릭하면 오른쪽 한 화면에서 텍스트 / PNG 등 이미지 / SPR / 기타 HEX를 확인할 수 있습니다.

원본 클라이언트와 PSC 백업은 수정하지 않습니다.
