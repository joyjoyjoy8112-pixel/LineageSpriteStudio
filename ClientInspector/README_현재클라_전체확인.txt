Lineage Client Inspector V1.0
현재 사용 중인 리니지 클라이언트 구조 확인용 읽기 전용 도구

[현재 클라 기준]
- Sprite00.idx/pak ~ Sprite15.idx/pak 자동 인식
- Image00.idx/pak ~ Image15.idx/pak 자동 인식
- Data / Tile / Text / Sound 및 기타 IDX/PAK 자동 검색
- LEGACY28, _EXT, _EXTB$, _IDX, _RMS, DES 인덱스 처리
- IDX 내부 파일명 / PAK / Offset / 원본크기 / 압축크기 / Flags 표시
- Sprite 파일명 byte 합계 % 16 기준 실제 분할팩 검사
- SPR 선택 시 프레임 수 / Palette 또는 RGB555 / FrameType / ZLIB 확인
- SPR 프레임 미리보기
- 선택 원본 추출
- 전체 클라 실제 파일 목록 표시
- 선택 실제 파일 SHA-256 계산
- TSV 저장

[기사 남자 확인]
1. 프로그램 실행
2. 현재 사용하는 클라이언트 폴더 선택
3. 전체 검사
4. '기사 61 바로 찾기' 클릭
5. 61-*.spr 전체가 어느 SpriteXX.idx/pak에 들어 있는지 확인
6. 검색 결과 SPR 분석으로 프레임/형식 확인
7. 원하는 SPR 선택 후 원본 추출

이 버전은 원본을 수정하지 않는 검사/추출 전용입니다.
