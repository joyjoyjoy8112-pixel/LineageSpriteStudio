Lineage Client Inspector V3.2

[핵심 수정]
V3.1에서 SPR 저장 시
"현재 PAK 형식은 _EXT 입니다"
오류로 저장이 차단되던 문제 수정.

[PAK SPR 저장 지원]
지원:
- 비암호화 LEGACY28
- 비암호화 _EXT

_EXT 저장 방식:
- 기존 엔트리 offset/size/flags 분석
- Flags=0: RAW 유지
- Flags=2 + CompressedSize>0: Brotli로 다시 압축
- FileSize = 원본 데이터 크기
- CompressedSize = 새 압축 데이터 크기
- Flags 유지
- 새 PAK offset 계산
- IDX offset/FileSize/CompressedSize/Flags 갱신

[안전]
- 저장 전에 IDX/PAK .sprbak_날짜시간 백업
- 알 수 없는 _EXT Flags는 저장 차단
- DES 암호화 PAK은 아직 직접 저장 차단
- _EXTB$, _IDX, _RMS 등 미검증 형식도 직접 저장 차단
- 기존 엔트리는 원본 stored bytes 그대로 복사

[SPR 기능 유지]
- 원본/수정본 좌우 비교
- 자동 프레임 재생
- 현재/전체 PNG 추출
- 현재 프레임 PNG 교체
- PNG 폴더 SPR 재생성
- 수정 SPR 불러오기
- 전체 SPR 색상 테스트
- 원본 Palette/RGB555, FrameType, ZLIB/RAW 유지
- 현재 수정 SPR 원본 저장

[이미지]
이미지 PAK 재등록도 비암호화 _EXT까지 동일하게 지원.
