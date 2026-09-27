Lineage Client Inspector V3.3

[이번 오류 수정]
V3.2에서 _EXT 내부 61-1.spr 저장 시
Flags=1 때문에 "미지원 압축 플래그" 오류가 발생하던 문제 수정.

[_EXT Flags 지원]
- Flags=0 : RAW 저장
- Flags=1 : RAW 저장 + Flags=1 유지
- Flags=2 : Brotli 재압축 + CompressedSize 갱신
- 그 외 알 수 없는 Flags는 계속 차단

[저장 검증 강화]
- PAK 재묶기 후 SpritePak으로 다시 열기
- 저장한 SPR 엔트리를 다시 추출
- 저장하려던 SPR 바이트와 완전히 동일한지 비교
- Inspector에서도 AnyPakScanner로 한 번 더 재추출/비교
- 두 단계 검증을 모두 통과해야 저장 완료 처리

[자동 복원]
- 저장 전에 IDX/PAK 백업 생성
- 저장/검증 도중 하나라도 실패하면
  백업 IDX/PAK로 자동 복원
- 상태바에 "SPR 저장 실패 - IDX/PAK 자동 복원 완료" 표시

[기존 기능 유지]
- 원본/수정 SPR 좌우 비교
- 전체 SPR 색상 테스트
- PNG 프레임/폴더로 수정
- ZLIB/RAW 유지
- Palette/RGB555 및 FrameType 유지
- LEGACY28 및 비암호화 _EXT 지원
