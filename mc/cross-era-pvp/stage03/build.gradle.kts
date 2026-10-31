plugins {
    java
}

group = "com.caoren.crossera"
version = "0.0.1-stage03"

repositories {
    mavenCentral()
    maven("https://repo.papermc.io/repository/maven-public/")
}

dependencies {
    compileOnly("io.papermc.paper:paper-api:26.3.build.223-stable")
    compileOnly("com.google.code.gson:gson:2.14.0")
}

java {
    toolchain.languageVersion.set(JavaLanguageVersion.of(25))
}

tasks.withType<JavaCompile>().configureEach {
    options.encoding = "UTF-8"
    options.release.set(25)
}

tasks.processResources {
    filteringCharset = "UTF-8"
    filesMatching("plugin.yml") {
        expand("version" to project.version)
    }
}

tasks.jar {
    archiveBaseName.set("CrossEraProbe")
    isPreserveFileTimestamps = false
    isReproducibleFileOrder = true
}
