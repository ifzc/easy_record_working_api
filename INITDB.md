-- 初始化数据库
CREATE DATABASE IF NOT EXISTS easy_record_working
DEFAULT CHARACTER SET utf8mb4
COLLATE utf8mb4_unicode_ci;

USE easy_record_working;

-- 租户表
CREATE TABLE IF NOT EXISTS tenants (
id CHAR(36) NOT NULL,
code VARCHAR(50) NOT NULL,
name VARCHAR(100) NOT NULL,
status VARCHAR(20) NOT NULL,
created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
PRIMARY KEY (id),
UNIQUE KEY uk_tenants_code (code)
) ENGINE=InnoDB;

-- 用户表
CREATE TABLE IF NOT EXISTS users (
id CHAR(36) NOT NULL,
tenant_id CHAR(36) NOT NULL,
account VARCHAR(100) NOT NULL,
password_hash VARCHAR(255) NOT NULL,
display_name VARCHAR(100) NULL,
role VARCHAR(20) NOT NULL,
status VARCHAR(20) NOT NULL,
created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
PRIMARY KEY (id),
UNIQUE KEY uk_users_tenant_account (tenant_id, account),
KEY idx_users_tenant (tenant_id),
CONSTRAINT fk_users_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB;

-- 员工表
CREATE TABLE IF NOT EXISTS employees (
id CHAR(36) NOT NULL,
tenant_id CHAR(36) NOT NULL,
name VARCHAR(50) NOT NULL,
type VARCHAR(10) NOT NULL,
is_active TINYINT(1) NOT NULL DEFAULT 1,
created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
PRIMARY KEY (id),
KEY idx_employees_tenant (tenant_id),
KEY idx_employees_name (name),
KEY idx_employees_active (is_active),
CONSTRAINT fk_employees_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB;

-- 记工表
CREATE TABLE IF NOT EXISTS time_entries (
id CHAR(36) NOT NULL,
tenant_id CHAR(36) NOT NULL,
employee_id CHAR(36) NOT NULL,
work_date DATE NOT NULL,
normal_hours DECIMAL(5,2) NOT NULL DEFAULT 8.00,
overtime_hours DECIMAL(5,2) NOT NULL DEFAULT 0.00,
created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
PRIMARY KEY (id),
UNIQUE KEY uk_time_entries_unique (tenant_id, employee_id, work_date),
KEY idx_time_entries_tenant_date (tenant_id, work_date),
KEY idx_time_entries_employee (employee_id),
KEY idx_time_entries_date_employee (work_date, employee_id),
CONSTRAINT fk_time_entries_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
CONSTRAINT fk_time_entries_employee FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE RESTRICT ON UPDATE
CASCADE
) ENGINE=InnoDB;

-- 初始化租户与管理员
SET @tenant_id = '6f9d7f31-4e68-4b5f-8b2e-6e2f4a2a3b01';
SET @user_id   = 'b4bdb8f1-3f8a-4f5a-9aa1-7b9b6c9d8e02';

INSERT INTO tenants (id, code, name, status, created_at, updated_at)
VALUES (@tenant_id, 'tenant-a', 'tenant-a', 'active', CURRENT_TIMESTAMP(3), CURRENT_TIMESTAMP(3));

-- 初始密码：123qwe
INSERT INTO users (id, tenant_id, account, password_hash, display_name, role, status, created_at, updated_at)
VALUES (
@user_id,
'admin',
@tenant_id,
'100000.RMV0twWKjOFup32Zw1OYYA==.RWgOD1JEaTKgPMwQ0V+SSqfpFzh3eCfEjuoeOMpx1zI=',
'admin',
'admin',
'active',
CURRENT_TIMESTAMP(3),
CURRENT_TIMESTAMP(3)
);