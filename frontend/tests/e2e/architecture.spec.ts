import { test, expect } from "@playwright/test";

test("admin gerencia usuários, treinos, fichas e exercícios; comum fica limitado à própria conta", async ({
  page,
}) => {
  const email = `hierarchy-${Date.now()}@example.invalid`,
    password = "Senha inicial longa 2026";
  await expect
    .poll(async () => (await page.request.get("/api/health/ready")).status(), {
      timeout: 30000,
    })
    .toBe(200);
  await page.goto("/");
  await page
    .getByLabel("E-mail", { exact: true })
    .fill(process.env.TREINOS_TEST_EMAIL!);
  await page
    .getByLabel("Senha", { exact: true })
    .fill(process.env.TREINOS_TEST_PASSWORD!);
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await page
    .getByRole("button", { name: "Administração", exact: true })
    .click();
  await expect(page.getByLabel("Usuário para gerenciar")).toContainText(
    process.env.TREINOS_TEST_EMAIL!,
  );
  await page.getByRole("button", { name: "Novo usuário", exact: true }).click();
  await page
    .getByLabel("Nome do usuário", { exact: true })
    .fill("Usuário da hierarquia");
  await page.getByLabel("E-mail do usuário", { exact: true }).fill(email);
  await page.getByLabel("Senha inicial", { exact: true }).fill(password);
  const createResponse = page.waitForResponse(
    (r) => r.url().endsWith("/api/users") && r.request().method() === "POST",
  );
  await page
    .getByRole("button", { name: "Criar usuário", exact: true })
    .click();
  const response = await createResponse;
  expect(response.status()).toBe(201);
  const user = await response.json();
  await expect(
    page.getByRole("heading", { name: "Dados de Usuário da hierarquia" }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "Treinos e fichas", exact: true }),
  ).toBeVisible();
  for (const name of ["Exercício um", "Exercício dois"]) {
    await page
      .getByRole("button", { name: "Novo exercício", exact: true })
      .click();
    await page.getByLabel("Nome", { exact: true }).fill(name);
    await page.getByLabel("Equipamento", { exact: true }).fill("Halteres");
    await page
      .getByLabel("Instruções", { exact: true })
      .fill("Mantenha a postura.\nFaça o movimento completo.");
    const saved = page.waitForResponse(
      (r) =>
        r.url().endsWith("/api/exercises") && r.request().method() === "POST",
    );
    await page.getByRole("button", { name: "Salvar", exact: true }).click();
    expect((await saved).status()).toBe(201);
    await expect(
      page.getByRole("heading", { name: "Novo exercício", exact: true }),
    ).toHaveCount(0);
  }
  for (const name of ["Treino um", "Treino dois"]) {
    await page
      .getByRole("button", { name: "Novo treino", exact: true })
      .click();
    await page.getByLabel("Nome do treino", { exact: true }).fill(name);
    await page
      .getByRole("button", { name: "Criar treino", exact: true })
      .click();
    await expect(page.getByLabel("Treino", { exact: true })).toContainText(
      name,
    );
    await expect(
      page.getByLabel("Nome do treino", { exact: true }),
    ).toHaveCount(0);
  }
  await page
    .getByLabel("Treino", { exact: true })
    .selectOption({ label: "Treino um" });
  await page.getByRole("button", { name: "Nova ficha", exact: true }).click();
  await page.getByLabel("Nome da ficha", { exact: true }).fill("Ficha A");
  await page.getByLabel("Código", { exact: true }).fill("A");
  const editor = page.locator(".sheet-editor");
  await editor
    .getByRole("button", { name: "Adicionar exercício", exact: true })
    .click();
  await editor
    .getByRole("button", { name: "Adicionar exercício", exact: true })
    .click();
  await editor
    .getByLabel("Exercício", { exact: true })
    .nth(1)
    .selectOption({ label: "Exercício dois" });
  await page.getByRole("button", { name: "Criar ficha", exact: true }).click();
  const sheetA = () =>
    page.getByRole("article", { name: "Ficha Ficha A", exact: true });
  await expect(sheetA()).toBeVisible();
  await expect(sheetA()).toContainText("2 exercícios");
  await sheetA()
    .getByRole("button", { name: "Ver ficha", exact: true })
    .click();
  const details = page.getByRole("region", { name: "Detalhes da ficha" });
  await expect(details).toContainText("Exercício um");
  await expect(details).toContainText("Exercício dois");
  await expect(details).toContainText("Equipamento: Halteres");
  await expect(details).toContainText("Mantenha a postura.");
  await page.getByRole("button", { name: "Fechar ficha", exact: true }).click();
  await sheetA()
    .getByRole("button", { name: "Editar ficha", exact: true })
    .click();
  await expect(page.getByLabel("Nome da ficha", { exact: true })).toHaveValue(
    "Ficha A",
  );
  await page
    .getByLabel("Nome da ficha", { exact: true })
    .fill("Ficha A revisada");
  await page
    .getByLabel("Orientações", { exact: true })
    .fill("Ficha atualizada pelo administrador.");
  await editor
    .getByRole("button", { name: "Descer exercício 1", exact: true })
    .click();
  await editor
    .getByRole("button", { name: "Remover item", exact: true })
    .last()
    .click();
  await editor
    .getByRole("button", { name: "Adicionar exercício", exact: true })
    .click();
  await editor.getByLabel("Séries", { exact: true }).nth(1).fill("4");
  await page
    .getByRole("button", { name: "Salvar alterações", exact: true })
    .click();
  const updated = page.getByRole("article", {
    name: "Ficha Ficha A revisada",
    exact: true,
  });
  await expect(updated).toContainText("Ficha atualizada pelo administrador.");
  await updated
    .getByRole("button", { name: "Editar ficha", exact: true })
    .click();
  await page
    .getByLabel("Nome da ficha", { exact: true })
    .fill("Alteração descartada");
  await editor.getByRole("button", { name: "Cancelar", exact: true }).click();
  await expect(updated).toBeVisible();
  await page.getByRole("button", { name: "Nova ficha", exact: true }).click();
  await page.getByLabel("Nome da ficha", { exact: true }).fill("Ficha B");
  await page.getByLabel("Código", { exact: true }).fill("B");
  await editor
    .getByRole("button", { name: "Adicionar exercício", exact: true })
    .click();
  await page.getByRole("button", { name: "Criar ficha", exact: true }).click();
  const sheetB = page.getByRole("article", {
    name: "Ficha Ficha B",
    exact: true,
  });
  await expect(sheetB).toBeVisible();
  await expect(updated).toBeVisible();
  page.once("dialog", (dialog) => dialog.accept());
  const deleted = page.waitForResponse(
    (r) =>
      r.request().method() === "DELETE" && r.url().includes("/api/sheets/"),
  );
  await sheetB
    .getByRole("button", { name: "Excluir ficha", exact: true })
    .click();
  expect((await deleted).status()).toBe(200);
  await expect(sheetB).toHaveCount(0);
  await expect(updated).toBeVisible();
  const scoped = { "X-Training-User": user.id };
  const bootstrap = await (
    await page.request.get("/api/sync/bootstrap", { headers: scoped })
  ).json();
  expect(bootstrap.exercises).toHaveLength(2);
  expect(bootstrap.plans).toHaveLength(2);
  const savedSheet = bootstrap.plans.find(
    (p: { name: string }) => p.name === "Treino um",
  ).templates[0];
  expect(savedSheet.name).toBe("Ficha A revisada");
  expect(savedSheet.rowVersion).toBe(2);
  expect(savedSheet.items).toHaveLength(2);
  expect(
    savedSheet.items.map(
      (item: { exerciseId: string }) =>
        bootstrap.exercises.find(
          (ex: { id: string }) => ex.id === item.exerciseId,
        ).name,
    ),
  ).toEqual(["Exercício dois", "Exercício um"]);
  expect(savedSheet.items[1].targetSets).toBe(4);
  expect(
    bootstrap.plans.find((p: { name: string }) => p.name === "Treino um")
      .templates,
  ).toHaveLength(1);
  expect(
    await (await page.request.get(`/api/users/${user.id}/trainings`)).json(),
  ).toHaveLength(2);
  const own = await (await page.request.get("/api/sync/bootstrap")).json();
  expect(
    own.exercises.some((e: { name: string }) => e.name === "Exercício um"),
  ).toBe(false);
  // Validate common-user permissions using a distinct login.
  const csrf = async () =>
    (await (await page.request.get("/api/auth/csrf")).json()).token as string;
  const post = async (path: string, data: object) =>
    page.request.post("/api" + path, {
      data,
      headers: { "X-CSRF-TOKEN": await csrf() },
    });
  const admin = await (await page.request.get("/api/auth/me")).json();
  await page.locator(".header-right button").last().click();
  await page.getByRole("button", { name: "Sair", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Entrar", exact: true }),
  ).toBeVisible();
  expect((await post("/auth/login", { email, password })).status()).toBe(200);
  expect(
    (
      await post("/auth/change-password", {
        oldPassword: password,
        newPassword: "Senha definitiva longa 2026",
      })
    ).status(),
  ).toBe(204);
  expect(
    (
      await post("/auth/login", {
        email,
        password: "Senha definitiva longa 2026",
      })
    ).status(),
  ).toBe(200);
  expect((await page.request.get("/api/users")).status()).toBe(403);
  expect(
    (
      await post("/users", {
        name: "Intruso",
        email: "intruso@example.invalid",
        initialPassword: password,
      })
    ).status(),
  ).toBe(403);
  expect(
    (
      await page.request.get("/api/sync/bootstrap", {
        headers: { "X-Training-User": admin.id },
      })
    ).status(),
  ).toBe(403);
  expect(
    (await page.request.get(`/api/users/${admin.id}/trainings`)).status(),
  ).toBe(403);
  const myself = await (await page.request.get(`/api/users/${user.id}`)).json();
  expect(
    (
      await page.request.put(`/api/users/${user.id}`, {
        headers: { "X-CSRF-TOKEN": await csrf() },
        data: {
          name: myself.displayName,
          email,
          role: "administrator",
          expectedVersion: myself.rowVersion,
        },
      })
    ).status(),
  ).toBe(403);
  await post("/auth/logout", {});
  await page.getByLabel("E-mail", { exact: true }).fill(email);
  await page
    .getByLabel("Senha", { exact: true })
    .fill("Senha definitiva longa 2026");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(
    page
      .locator(".header-right")
      .getByRole("button", { name: "Usuário da hierarquia", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Administração", exact: true }),
  ).toHaveCount(0);
  await page.getByRole("button", { name: "Fichas", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Fichas de treino", exact: true }),
  ).toBeVisible();
  await expect(page.getByLabel("Treino", { exact: true })).toContainText(
    "Treino um",
  );
  await page
    .getByLabel("Treino", { exact: true })
    .selectOption({ label: "Treino um" });
  await expect(
    page.getByRole("article", { name: "Ficha Ficha A revisada", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Nova ficha", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Exercícios", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Catálogo de exercícios", exact: true }),
  ).toBeVisible();
  await page.goto("/admin");
  await expect(
    page.getByText("Acesso restrito ao administrador.", { exact: true }),
  ).toBeVisible();
});
