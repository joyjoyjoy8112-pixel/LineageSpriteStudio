Lineage Client Inspector V3.4

[캐릭터 선택 팅김 원인 대응]
V3.3까지는 PAK 내부 SPR 하나를 저장해도
SpriteXX.pak 전체 엔트리를 새 PAK으로 재조립하고 모든 offset을 다시 만들었습니다.
클라이언트는 Inspector 재추출 검증을 통과해도 전체 PAK 배치 변화 때문에 런타임에서 팅길 수 있습니다.

[V3.4 최소 변경 저장]
SPR 저장 시 PAK 전체 재묶기 금지.

1) 새 저장 데이터가 기존 엔트리 stored size 이하
- 원래 Offset 위치에 그대로 덮어쓰기
- 다른 엔트리 Offset 변경 없음
- IDX의 다른 레코드 변경 없음
- 크기가 정확히 같으면 mode=IN_PLACE_EXACT

2) 새 저장 데이터가 기존보다 작음
- 원래 Offset에 저장
- 남는 영역은 0으로 정리
- 대상 엔트리 Size 필드만 갱신
- mode=IN_PLACE_SHORTER

3) 새 저장 데이터가 기존보다 큼
- 기존 PAK 전체는 그대로 유지
- 수정한 엔트리 하나만 PAK 끝에 append
- 4-byte 정렬
- 대상 엔트리의 Offset/Size/CompressedSize/Flags만 갱신
- mode=APPEND_TARGET_ONLY

[_EXT Flags]
- Flags=0: RAW
- Flags=1: RAW + Flags=1 그대로 유지
- Flags=2: Brotli 재압축
- 미지원 Flags는 차단

[IDX 보존]
_EXT IDX 전체를 새로 생성하지 않음.
원본 IDX 바이트를 그대로 유지하고 대상 record의
Offset/FileSize/CompressedSize/Flags 필드만 직접 수정.

[검증/복원]
- 저장 전 IDX/PAK 자동 백업
- 저장 후 SpritePak 재추출 검증
- AnyPakScanner 2차 재추출 검증
- 실패 시 IDX/PAK 백업으로 자동 롤백

[테스트]
먼저 원본 복원 상태에서 노담 창고 NPC의 61-1.spr 하나만
'전체 SPR 색상 테스트' → '현재 수정 SPR 원본에 저장'으로 테스트.
V3.4 상태바의 mode 값을 확인.
