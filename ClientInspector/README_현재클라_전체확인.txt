Lineage Client Inspector V1.2
현재 사용 중인 리니지 클라이언트 전체 확인/미리보기 도구

[V1.2 핵심]
- '모든 파일 보기' 탭 추가
- 실제 클라이언트 폴더의 모든 파일 + 모든 IDX/PAK 내부 항목을 한 목록으로 표시
- 파일 종류에 따라 자동 미리보기
  * HTML/HTM/TXT/XML/JSON/INI/CFG/JS/CSS/LUA/CSV/LOG/MD/YML: 원문 텍스트
  * PNG/BMP/JPG/JPEG/GIF/ICO: 실제 이미지
  * SPR: 프레임별 이미지, 프레임 수, Palette/RGB555, FrameType, ZLIB
  * 그 외 모든 바이너리: HEX + ASCII
- 파일명/경로/확장자 통합 검색
- 실제 파일과 PAK 내부 파일 모두 선택 원본 저장 가능
- 알 수 없는 형식도 빈 화면 대신 HEX로 반드시 확인 가능

[V1.1 기능 유지]
- HTML/텍스트 전용 탭
- UTF-8 / UTF-16 / CP949(EUC-KR) 자동 판독
- 본문 검색

[기존 검사 기능]
- Sprite00~15, Image00~15 및 Data/Tile/Text/Sound 등 IDX/PAK 자동 인식
- LEGACY28, _EXT, _EXTB$, _IDX, _RMS, DES 처리
- 내부 파일명 / PAK / Offset / 크기 / 압축정보 / Flags
- 기사 61 바로 찾기
- SPR 원본 추출 및 미리보기
- 실제 클라 전체 파일 목록 및 선택 SHA-256

이 버전은 읽기/검사/추출 전용이며 원본 클라이언트를 수정하지 않습니다.
