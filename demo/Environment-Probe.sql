SELECT SERVERPROPERTY('ProductVersion') AS productVersion, SERVERPROPERTY('Edition') AS edition, SERVERPROPERTY('EngineEdition') AS engineEdition;
SELECT name,compatibility_level,collation_name,is_query_store_on FROM sys.databases WHERE name LIKE N'AlyvoStage5Demo%';
SELECT f.name,v.supports_sparse_files FROM sys.database_files f OUTER APPLY sys.dm_os_volume_stats(DB_ID(),f.file_id) v WHERE f.type=0;
