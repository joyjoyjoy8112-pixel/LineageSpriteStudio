Lineage Client Inspector V1.8

[DB SQL 행 단위 통합 검색]
- Navicat에서 내보낸 .sql dump를 선택하면 통합 검색 시 INSERT 행 단위로 검색
- 결과에 실제 테이블명과 SQL 라인 번호 표시
- 동일 검색어가 여러 테이블에 있으면 각각 별도 결과로 표시
- 결과 클릭 시 해당 INSERT SQL 한 줄을 오른쪽에서 바로 확인
- 클라이언트 / IDX·PAK 내부 / Navicat SQL dump를 한 검색어로 동시에 검색

예:
3000209 검색 시
- DB SQL | etcitem | line ...
- DB SQL | shop | line ...
- DB SQL | shop_copy | line ...
처럼 분리 표시

[노이즈 제거 유지]
- EXE/DLL/BIN/DAT 등의 원시 HEX 우연 일치는 검색 결과에서 제외
- 텍스트/SQL/HTML/XML/JSON 등 의미 있는 정보 위주로 검색

[미리보기]
- 텍스트/SQL → 원문
- 이미지 → 이미지
- SPR → 프레임
- 기타 → HEX

원본 클라이언트와 DB 백업은 수정하지 않습니다.
