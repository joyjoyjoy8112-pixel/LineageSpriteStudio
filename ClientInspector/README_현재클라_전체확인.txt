Lineage Client Inspector V1.5

[통합 한 화면]
- 왼쪽: 클라이언트 실제 파일 + IDX/PAK 내부 + Navicat/DB 백업
- 오른쪽: 선택 항목 자동 미리보기
- HTML/TXT/XML/JSON/INI/JS/CSS/LUA → 텍스트
- PNG/BMP/JPG/JPEG/GIF/ICO/TIF/TIFF → 이미지
- SPR → 프레임
- 기타 → HEX + ASCII
- 검색창 하나로 전체 검색

[Navicat PSC]
- 상단 '나비캣 PSC 선택' 버튼
- .psc/.nb3/.sql/.db/.sqlite/.bak/.dump 선택 가능
- .psc/.nb3가 표준 ZIP 계열로 열리면 내부 항목까지 왼쪽 목록에 표시
- 내부 텍스트/문자열은 검색 대상에 포함
- 일반 ZIP으로 직접 안 열리는 PSC도 원본 문자열 + HEX 확인 가능
- 선택 원본 저장 / SHA-256 확인 가능

[기존]
- Sprite00~15 / Image00~15 / Data / Tile / Text / Sound 등 IDX/PAK 자동 검사
- 기사 61은 검색창에 61- 입력
- 원본 클라이언트는 수정하지 않는 읽기/검사/추출 전용
