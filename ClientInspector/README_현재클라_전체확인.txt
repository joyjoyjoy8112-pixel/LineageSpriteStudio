Lineage Client Inspector V1.3
현재 사용 중인 리니지 클라이언트 전체 확인/미리보기 도구

[V1.3 이미지 확인 강화]
- 별도 '이미지 보기' 탭 추가
- 실제 클라 폴더와 IDX/PAK 내부의 PNG/BMP/JPG/JPEG/GIF/ICO/TIF/TIFF 목록 표시
- 클릭 즉시 오른쪽에 이미지 미리보기
- PNG/JPEG/GIF/BMP/ICO는 파일 시그니처도 검사
- 확장자가 이상해도 이미지 시그니처가 맞으면 '모든 파일 보기'에서 이미지로 표시
- 이미지 표시 실패 시 파일 HEAD 16바이트와 오류 원인을 화면에 표시
- 선택 이미지 원본 저장 가능

[V1.2 기능 유지]
- '모든 파일 보기'에서 실제 파일 + IDX/PAK 내부 항목 전체 표시
- HTML/텍스트는 원문
- SPR은 프레임
- 일반 이미지는 이미지
- 그 외 파일은 HEX+ASCII
- 파일명/경로/확장자 통합 검색

[V1.1 기능 유지]
- HTML/텍스트 전용 탭
- UTF-8 / UTF-16 / CP949(EUC-KR) 자동 판독
- 본문 검색

[기존 검사 기능]
- Sprite00~15, Image00~15 및 Data/Tile/Text/Sound 등 IDX/PAK 자동 인식
- LEGACY28, _EXT, _EXTB$, _IDX, _RMS, DES 처리
- 기사 61 바로 찾기
- SPR 미리보기/원본 추출
- 전체 파일 목록 및 선택 SHA-256

이 버전은 읽기/검사/추출 전용이며 원본 클라이언트를 수정하지 않습니다.
