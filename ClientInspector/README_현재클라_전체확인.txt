Lineage Client Inspector V3.1

[SPR 수정/비교/저장]
SPR 선택 시 오른쪽 화면을 원본 / 수정본 2쪽으로 표시합니다.
- 왼쪽: 원본 SPR 프레임
- 오른쪽: 수정 SPR 프레임
- 같은 프레임 번호를 동시 표시
- 자동 재생으로 원본/수정본을 동시에 비교

[SPR 정보 표시]
- 총 프레임 수
- Palette / RGB555
- Palette 색상 수
- FrameType
- RAW / ZLIB
- 원본 파일 크기
- 실제 파일 위치 또는 PAK 위치
- 수정 SPR 예상 저장 크기

[SPR 추출]
- 현재 프레임 PNG 추출
- 전체 프레임 PNG 추출
- 저장 위치:
  프로그램폴더\Extracted\SPR\SPR이름\
- frame_000.png, frame_001.png ... 자연 순서로 저장

[SPR 수정]
- 현재 프레임 PNG 불러오기
  * 원본 프레임과 가로/세로가 같아야 적용
  * 나머지 프레임과 함께 SPR 재생성
- PNG 폴더로 SPR 재생성
  * PNG 수 = 원본 프레임 수 필수
  * 각 PNG 크기 = 대응 원본 프레임 크기 필수
  * 원본 Palette/RGB555 방식과 FrameType 유지
- 수정 SPR 불러오기
  * 원본과 프레임 수가 같은 SPR만 허용
- 전체 SPR 색상 테스트
  * 원본은 변경하지 않고 오른쪽 수정본에만 적용
  * Palette SPR은 palette 값을 직접 변경
  * RGB555 SPR은 block pixel 값을 직접 변경
  * 블록 위치/프레임 구조는 그대로 유지
- 수정본 초기화

[현재 수정 SPR 원본에 저장]
일반 .spr 파일:
- 최초 원본을 <파일>.spr.bak 로 보관
- 저장 직전 상태도 .spr.before_날짜시간 으로 보관
- 수정 SPR 저장

PAK 내부 .spr:
- 안전 검증된 비암호화 LEGACY28 PAK만 직접 저장
- 적용 전 IDX/PAK 모두 .sprbak_날짜시간 백업
- 수정 SPR로 PAK 재묶기
- IDX offset/size 갱신
- 저장 후 PAK 스캐너 다시 로드

[원본 형식 유지]
- 원본이 ZLIB이면 수정 SPR도 ZLIB로 저장
- 원본이 RAW면 RAW로 저장
- PNG 재생성 시 원본 Palette/RGB555 모드와 FrameType 사용

[기존 기능 유지]
- 이미지 원본/수정본 비교 및 색상 테스트/저장
- 선택 추출
- 자연 숫자 정렬
- 61- 접두어 검색
- 클라 + 서버 + Navicat DB 통합 검색
- 원본 텍스트 수정/복원
- Java 단일/전체 컴파일
