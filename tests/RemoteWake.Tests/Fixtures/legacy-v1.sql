-- Esquema criado pela versão 0.1 (EnsureCreated + upgrade aditivo "v1"),
-- extraído com pg_dump --schema-only de uma instalação real.
CREATE TABLE public."Machines" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "MacAddress" text NOT NULL,
    "Hostname" text,
    "BroadcastAddress" text NOT NULL,
    "WolPort" integer NOT NULL,
    "WakeMethod" integer NOT NULL,
    "LastWakeRequestedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "OwnerId" uuid NOT NULL,
    "AgentKeyVersion" integer NOT NULL
);
CREATE TABLE public."SchemaVersions" (
    "Version" integer NOT NULL,
    "AppliedAt" timestamp with time zone DEFAULT now() NOT NULL
);
CREATE TABLE public."Users" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "Email" text NOT NULL,
    "PasswordHash" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL
);
CREATE TABLE public."WakeAttempts" (
    "Id" uuid NOT NULL,
    "MachineId" uuid NOT NULL,
    "Succeeded" boolean NOT NULL,
    "Action" text NOT NULL,
    "Message" text NOT NULL,
    "RequestedAt" timestamp with time zone NOT NULL
);
ALTER TABLE ONLY public."Machines"
    ADD CONSTRAINT "PK_Machines" PRIMARY KEY ("Id");
ALTER TABLE ONLY public."Users"
    ADD CONSTRAINT "PK_Users" PRIMARY KEY ("Id");
ALTER TABLE ONLY public."WakeAttempts"
    ADD CONSTRAINT "PK_WakeAttempts" PRIMARY KEY ("Id");
ALTER TABLE ONLY public."SchemaVersions"
    ADD CONSTRAINT "SchemaVersions_pkey" PRIMARY KEY ("Version");
CREATE INDEX "IX_Machines_OwnerId" ON public."Machines" USING btree ("OwnerId");
CREATE UNIQUE INDEX "IX_Users_Email" ON public."Users" USING btree ("Email");
CREATE INDEX "IX_WakeAttempts_MachineId" ON public."WakeAttempts" USING btree ("MachineId");
ALTER TABLE ONLY public."Machines"
    ADD CONSTRAINT "FK_Machines_Users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES public."Users"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY public."WakeAttempts"
    ADD CONSTRAINT "FK_WakeAttempts_Machines_MachineId" FOREIGN KEY ("MachineId") REFERENCES public."Machines"("Id") ON DELETE CASCADE;
INSERT INTO public."SchemaVersions" ("Version") VALUES (1);
