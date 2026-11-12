import java.util.zip.ZipFile

plugins { java }

group = "com.caoren.crossera"
version = "0.1.0-dev"

repositories {
    mavenCentral()
    maven("https://repo.papermc.io/repository/maven-public/")
}

val paperApi = "io.papermc.paper:paper-api:26.3.build.223-stable"
val ocmJar = providers.gradleProperty("ocmJar").map { file(it) }
    .orElse(layout.projectDirectory.file("libs/OldCombatMechanics.jar").asFile)

dependencies {
    compileOnly(paperApi)
    compileOnly("com.google.code.gson:gson:2.14.0")
    compileOnly(files(ocmJar))
    testImplementation("org.junit.jupiter:junit-jupiter:5.13.4")
    testImplementation(paperApi)
    // API boundary tests use the pinned local OCM types; this is not a packaged dependency.
    testImplementation(files(ocmJar))
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

java { toolchain.languageVersion.set(JavaLanguageVersion.of(25)) }
tasks.withType<JavaCompile>().configureEach {
    options.encoding = "UTF-8"
    options.release.set(25)
}

val verifyOcm = tasks.register("verifyOcm") {
    inputs.file(ocmJar)
    doLast {
        val jar = ocmJar.get()
        check(jar.isFile) { "Provide OCM v2.8.0 as libs/OldCombatMechanics.jar or -PocmJar=..." }
        ZipFile(jar).use { zip ->
            check(zip.getEntry("kernitus/plugin/OldCombatMechanics/api/OldCombatMechanicsAPI.class") != null)
            val descriptor = zip.getInputStream(zip.getEntry("plugin.yml")).bufferedReader().use { it.readText() }
            check(Regex("(?m)^version:\\s*['\"]?2\\.8\\.0['\"]?\\s*$").containsMatchIn(descriptor)) {
                "This experimental backend is pinned to OCM v2.8.0"
            }
        }
    }
}
tasks.compileJava { dependsOn(verifyOcm) }
tasks.test { useJUnitPlatform() }
tasks.processResources {
    filteringCharset = "UTF-8"
    filesMatching("plugin.yml") { expand("version" to project.version) }
}
tasks.jar {
    archiveBaseName.set("CrossEraCombat")
    from("LICENSE") { into("META-INF"); rename { "LICENSE" } }
    isPreserveFileTimestamps = false
    isReproducibleFileOrder = true
}
